using UtfUnknown;

namespace FileTrace.Core.Extraction;

/// <summary>
/// 纯文本 / 标记语言 / 代码 / 配置类文件的内容提取器。
/// 覆盖 txt/md/log/html/json/xml/yaml/py/java/c/cpp/js/ts/css/ini/cfg/sql/bat/sh/ps1 等。
///
/// 中文系统关键坑：这些文件不一定是 UTF-8 编码，大量老文件（尤其是 Windows 上用记事本
/// 保存的中文文档、旧项目代码注释）是 GBK/GB2312/GB18030 编码。这里用 UTF.Unknown 库
/// 自动检测编码后再解码，避免搜索/预览出现乱码导致匹配不到关键词。
/// </summary>
public sealed class PlainTextExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[]
    {
        "txt", "md", "log", "html", "json", "xml", "yaml", "ini", "cfg", "sql",
        "bat", "sh", "ps1", "py", "java", "c", "cpp", "js", "ts", "css", "csv",
    };

    /// <summary>
    /// 单文件读取的最大字节数（用于编码检测采样与全文读取的安全上限）。
    /// 若文件用于内容提取，调用方（索引任务）已经在此之前按 MaxFileSizeForContentBytes 过滤过大文件，
    /// 这里只是双重保险。
    /// </summary>
    private const int MaxReadBytes = 64 * 1024 * 1024; // 64MB

    public async Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return ExtractResult.Ok(string.Empty);
            }
            if (fileInfo.Length > MaxReadBytes)
            {
                return ExtractResult.Fail($"文件超过 {MaxReadBytes / 1024 / 1024}MB 读取上限，跳过内容提取");
            }

            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            var detected = CharsetDetector.DetectFromBytes(bytes)?.Detected;
            System.Text.Encoding encoding = detected?.Encoding ?? System.Text.Encoding.UTF8;

            string text = encoding.GetString(bytes);
            return ExtractResult.Ok(text);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ExtractResult.Fail("无权限访问文件: " + ex.Message);
        }
        catch (IOException ex)
        {
            return ExtractResult.Fail("文件读取IO错误(可能被占用或损坏): " + ex.Message);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("未知错误: " + ex.Message);
        }
    }
}
