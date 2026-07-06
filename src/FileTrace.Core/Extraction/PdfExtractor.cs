using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace FileTrace.Core.Extraction;

/// <summary>
/// PDF 内容提取器，使用 PdfPig 逐页提取文本。
///
/// 关键容错点：
///   - 加密 PDF：PdfPig 在无法解密时会抛出 <see cref="PdfDocumentEncryptedException"/>，
///     先尝试用空密码打开（很多"加密"PDF其实只是限制打印/编辑，内容本身不加密，空密码即可读取），
///     否则明确标记为"已加密，跳过内容提取"，不阻断整个索引任务；
///   - 扫描版 PDF（图片形式，没有文本层）：PdfPig 会正常打开但每页 Text 为空字符串，
///     这属于正常结果而非错误——OCR识别是可选功能，默认关闭，此处不做处理；
///   - 单页解析异常（字体损坏、非标准编码等）：逐页捕获，避免一页坏掉导致整份文档提取失败。
/// </summary>
public sealed class PdfExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "pdf" };

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        return Task.Run(() => Extract(filePath, cancellationToken), cancellationToken);
    }

    private static ExtractResult Extract(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return TryOpenAndExtract(filePath, password: null, cancellationToken);
        }
        catch (PdfDocumentEncryptedException)
        {
            // 很多所谓“加密”PDF只是设置了权限密码（禁止打印/复制），内容本身用空用户密码即可解密，
            // 先尝试空密码兜底一次，仍失败才真正判定为需要密码、无法提取。
            try
            {
                return TryOpenAndExtract(filePath, password: string.Empty, cancellationToken);
            }
            catch (Exception)
            {
                return ExtractResult.Fail("PDF 已加密，需要密码才能提取内容");
            }
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("pdf 解析失败: " + ex.Message);
        }
    }

    private static ExtractResult TryOpenAndExtract(string filePath, string? password, CancellationToken cancellationToken)
    {
        var options = new ParsingOptions
        {
            UseLenientParsing = true, // 尽量容忍不完全符合规范的 PDF（现实中大量老旧工具生成的PDF并不严格合规）
        };
        if (password is not null)
        {
            options.Password = password;
        }

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = PdfDocument.Open(fs, options);

        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }
            catch (Exception)
            {
                // 单页解析失败（例如字体表损坏）不应影响其余页面的提取结果，跳过即可。
            }
        }

        return ExtractResult.Ok(sb.ToString());
    }
}
