using NPOI.HWPF;
using NPOI.XWPF.UserModel;

namespace FileTrace.Core.Extraction;

/// <summary>
/// Word 文档内容提取器，同时覆盖新旧两种格式：
///   - .docx（Office 2007+，基于 XML）—— 使用 NPOI.XWPF
///   - .doc（Office 97-2003，二进制 OLE2 格式）—— 使用 NPOI.HWPF（需要额外的 ScratchPad.NPOI.HWPF 包）
///
/// 关键容错点：加密文档（打开时要求密码）、损坏的 OLE 结构、非标准的 .doc 文件（例如
/// 有些老系统导出的“伪 doc”其实是 RTF 或纯文本）都必须被捕获，不能让异常向上抛出。
/// </summary>
public sealed class WordExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "doc", "docx" };

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return Task.Run(() => ext == "docx" ? ExtractDocx(filePath) : ExtractDoc(filePath), cancellationToken);
    }

    private static ExtractResult ExtractDocx(string filePath)
    {
        // 注意：NPOI 2.5.6 里 XWPFDocument 只实现了 NPOI.Util.ICloseable（Close()方法），
        // 并未实现 System.IDisposable，因此不能用 `using var doc = ...`，
        // 需要手动在 finally 里调用 Close() 释放底层 OPC 包资源。
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        XWPFDocument? doc = null;
        try
        {
            doc = new XWPFDocument(fs);

            var sb = new System.Text.StringBuilder();

            foreach (var para in doc.Paragraphs)
            {
                if (!string.IsNullOrEmpty(para.Text))
                {
                    sb.AppendLine(para.Text);
                }
            }

            // 表格内容也需要索引，很多报表类 docx 主要内容都在表格里
            foreach (var table in doc.Tables)
            {
                foreach (var row in table.Rows)
                {
                    var cells = row.GetTableCells().Select(c => c.GetText()).Where(t => !string.IsNullOrWhiteSpace(t));
                    if (cells.Any())
                    {
                        sb.AppendLine(string.Join(" | ", cells));
                    }
                }
            }

            return ExtractResult.Ok(sb.ToString());
        }
        catch (Exception ex) when (IsLikelyEncrypted(ex))
        {
            return ExtractResult.Fail("文档可能已加密或格式不受支持: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("docx 解析失败: " + ex.Message);
        }
        finally
        {
            doc?.Close();
        }
    }

    private static ExtractResult ExtractDoc(string filePath)
    {
        // 同上：HWPFDocument 也只实现 ICloseable，不是 IDisposable。
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        HWPFDocument? doc = null;
        try
        {
            doc = new HWPFDocument(fs);
            var range = doc.GetRange();
            return ExtractResult.Ok(range.Text);
        }
        catch (Exception ex) when (IsLikelyEncrypted(ex))
        {
            return ExtractResult.Fail("文档可能已加密或格式不受支持: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail("doc 解析失败: " + ex.Message);
        }
        finally
        {
            doc?.Close();
        }
    }

    /// <summary>
    /// NPOI 对加密文档一般会抛出通用异常（不同版本类型不完全一致），这里通过关键字粗略识别，
    /// 保证至少能给用户一个可读的失败原因，而不是让整个索引任务崩溃。
    /// </summary>
    private static bool IsLikelyEncrypted(Exception ex) =>
        ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
