using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FileTrace.Core.Extraction;

/// <summary>
/// EPUB 电子书内容提取器。
///
/// EPUB 本质是一个 ZIP 容器，标准结构是：
///   META-INF/container.xml  —— 指向 OPF 包文档（.opf）的路径
///   *.opf                   —— 包文档：manifest（列出所有资源文件）+ spine（阅读顺序）
///   各章节的 .xhtml/.html 文件 —— 正文内容
///
/// 提取策略：按 spine 阅读顺序依次读取每个 xhtml/html 章节文件，剥离标签后拼接全文。
/// 完全不依赖第三方 EPUB 库（.NET 内置 System.IO.Compression 即可解压 ZIP），
/// 保持依赖清单精简。
///
/// 关键容错点：
///   - container.xml 缺失或指向的 OPF 不存在（部分"伪 EPUB"/损坏文件）——退化为
///     直接遍历压缩包内所有 .xhtml/.html/.htm 文件，尽量兜底提取到内容；
///   - OPF 里 manifest/spine 结构不规范或缺失——同样退化为遍历模式；
///   - 单个章节文件损坏或编码异常——单独捕获跳过，不影响其余章节。
/// </summary>
public sealed class EpubExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "epub" };

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceCollapseRegex = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        return Task.Run(() => Extract(filePath, cancellationToken), cancellationToken);
    }

    private static ExtractResult Extract(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            List<string> orderedChapterPaths = ResolveSpineOrder(archive) ?? FallbackAllHtmlEntries(archive);

            var sb = new StringBuilder();
            foreach (var entryPath in orderedChapterPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.GetEntry(entryPath);
                if (entry is null)
                {
                    continue;
                }

                try
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    string html = reader.ReadToEnd();
                    string text = StripHtmlTags(html);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }
                catch
                {
                    // 单章节解析失败不影响其余章节
                }
            }

            return ExtractResult.Ok(sb.ToString());
        }
        catch (InvalidDataException ex)
        {
            // 不是合法 ZIP 结构，或 ZIP 已损坏
            return ExtractResult.Fail("epub 文件已损坏或不是合法的 ZIP/EPUB 格式: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("epub 解析失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 按标准 EPUB 结构解析出 spine 阅读顺序对应的章节文件路径列表。
    /// 任何一步失败都返回 null，交给调用方走兜底遍历模式。
    /// </summary>
    private static List<string>? ResolveSpineOrder(ZipArchive archive)
    {
        try
        {
            var containerEntry = archive.GetEntry("META-INF/container.xml");
            if (containerEntry is null)
            {
                return null;
            }

            using (var containerStream = containerEntry.Open())
            {
                var containerXml = XDocument.Load(containerStream);
                XNamespace containerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
                string? opfPath = containerXml
                    .Descendants(containerNs + "rootfile")
                    .FirstOrDefault()
                    ?.Attribute("full-path")?.Value;

                if (string.IsNullOrEmpty(opfPath))
                {
                    return null;
                }

                var opfEntry = archive.GetEntry(opfPath);
                if (opfEntry is null)
                {
                    return null;
                }

                string opfDirectory = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;

                using var opfStream = opfEntry.Open();
                var opfXml = XDocument.Load(opfStream);
                XNamespace opfNs = "http://www.idpf.org/2007/opf";

                // manifest: id -> href
                var manifestItems = opfXml.Descendants(opfNs + "item")
                    .Select(e => new
                    {
                        Id = (string?)e.Attribute("id"),
                        Href = (string?)e.Attribute("href"),
                        MediaType = (string?)e.Attribute("media-type"),
                    })
                    .Where(x => x.Id is not null && x.Href is not null)
                    .ToDictionary(x => x.Id!, x => x.Href!);

                // spine: 有序的 itemref 列表，按此顺序拼出章节路径
                var spineIdRefs = opfXml.Descendants(opfNs + "itemref")
                    .Select(e => (string?)e.Attribute("idref"))
                    .Where(idref => idref is not null)
                    .Select(idref => idref!)
                    .ToList();

                if (spineIdRefs.Count == 0 || manifestItems.Count == 0)
                {
                    return null;
                }

                var result = new List<string>();
                foreach (var idref in spineIdRefs)
                {
                    if (manifestItems.TryGetValue(idref, out var href))
                    {
                        string combined = string.IsNullOrEmpty(opfDirectory) ? href : $"{opfDirectory}/{href}";
                        result.Add(NormalizeZipPath(combined));
                    }
                }

                return result.Count > 0 ? result : null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>兜底模式：找不到有效 OPF/spine 结构时，直接遍历压缩包内所有 html/xhtml 条目。</summary>
    private static List<string> FallbackAllHtmlEntries(ZipArchive archive)
    {
        return archive.Entries
            .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                     || e.FullName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.FullName)
            .ToList();
    }

    private static string NormalizeZipPath(string path)
    {
        // 处理 "a/b/../c.xhtml" 这类相对路径片段，ZipArchive 的条目名不支持 ".."，需要手动折叠
        var parts = path.Replace('\\', '/').Split('/');
        var stack = new Stack<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (stack.Count > 0) stack.Pop();
            }
            else if (part is not "." and not "")
            {
                stack.Push(part);
            }
        }
        return string.Join('/', stack.Reverse());
    }

    private static string StripHtmlTags(string html)
    {
        string noTags = TagRegex.Replace(html, " ");
        string decoded = System.Net.WebUtility.HtmlDecode(noTags);
        string collapsed = WhitespaceCollapseRegex.Replace(decoded, " ");
        return collapsed;
    }
}
