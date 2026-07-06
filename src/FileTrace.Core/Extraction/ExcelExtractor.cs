using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FileTrace.Core.Extraction;

/// <summary>
/// Excel 表格内容提取器，覆盖新旧两种格式：
///   - .xlsx（Office 2007+，基于 XML）—— 使用 NPOI.XSSF
///   - .xls（Office 97-2003，二进制 BIFF 格式）—— 使用 NPOI.HSSF
///
/// .csv 不在本提取器范围内，由 <see cref="PlainTextExtractor"/> 以纯文本方式处理（带编码检测）。
///
/// 关键容错点：
///   - 公式单元格可能因引用外部工作簿/自定义函数而求值失败，需要单独兜底，不能中断整表提取；
///   - 加密的工作簿（.xls 的 RC4/CryptoAPI 加密、.xlsx 的 ECMA-376 Agile 加密）会在打开阶段直接抛异常；
///   - 极端稀疏或超大表格（几十万行）要避免不必要的内存膨胀，这里只做顺序流式拼接文本，不做二次缓存。
/// </summary>
public sealed class ExcelExtractor : IContentExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { "xls", "xlsx" };

    public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        return Task.Run(() => ext == "xlsx" ? ExtractWorkbook(filePath, isXlsx: true) : ExtractWorkbook(filePath, isXlsx: false), cancellationToken);
    }

    private static ExtractResult ExtractWorkbook(string filePath, bool isXlsx)
    {
        // 注意：NPOI 2.5.6 里 HSSFWorkbook / XSSFWorkbook 都只实现了 NPOI.Util.ICloseable
        // （Close() 方法），并未实现 System.IDisposable，因此不能用 `using var wb = ...`，
        // 需要手动在 finally 里调用 Close() 释放底层资源（与 WordExtractor 处理方式一致）。
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        IWorkbook? workbook = null;
        try
        {
            workbook = isXlsx ? new XSSFWorkbook(fs) : new HSSFWorkbook(fs);

            var sb = new System.Text.StringBuilder();
            var formatter = new DataFormatter();

            for (int sheetIndex = 0; sheetIndex < workbook.NumberOfSheets; sheetIndex++)
            {
                ISheet sheet = workbook.GetSheetAt(sheetIndex);
                if (sheet is null)
                {
                    continue;
                }

                for (int rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    IRow? row = sheet.GetRow(rowIndex);
                    if (row is null)
                    {
                        continue;
                    }

                    var cellTexts = new List<string>();
                    for (int cellIndex = row.FirstCellNum; cellIndex >= 0 && cellIndex < row.LastCellNum; cellIndex++)
                    {
                        ICell? cell = row.GetCell(cellIndex);
                        if (cell is null)
                        {
                            continue;
                        }

                        string text = SafeFormatCell(cell, formatter);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            cellTexts.Add(text);
                        }
                    }

                    if (cellTexts.Count > 0)
                    {
                        sb.AppendLine(string.Join(" | ", cellTexts));
                    }
                }
            }

            return ExtractResult.Ok(sb.ToString());
        }
        catch (Exception ex) when (IsLikelyEncrypted(ex))
        {
            return ExtractResult.Fail("工作簿可能已加密或格式不受支持: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ExtractResult.Fail((isXlsx ? "xlsx" : "xls") + " 解析失败: " + ex.Message);
        }
        finally
        {
            workbook?.Close();
        }
    }

    /// <summary>
    /// 单元格取文本要格外小心：公式单元格求值可能抛异常（例如引用了未加载的外部工作簿、
    /// 自定义/不支持的函数），这里单独捕获，保证一个坏公式不会导致整个文件、
    /// 甚至整个索引任务失败——最多丢失这一个单元格的内容。
    /// </summary>
    private static string SafeFormatCell(ICell cell, DataFormatter formatter)
    {
        try
        {
            if (cell.CellType == CellType.Formula)
            {
                try
                {
                    return formatter.FormatCellValue(cell, cell.Sheet.Workbook.GetCreationHelper().CreateFormulaEvaluator());
                }
                catch
                {
                    // 公式求值失败时退化为读取缓存的公式结果值，仍不行则读取原始公式文本，
                    // 三层兜底确保这个单元格至少能贡献一些可搜索的文本。
                    try
                    {
                        return formatter.FormatCellValue(cell);
                    }
                    catch
                    {
                        return cell.CellFormula ?? string.Empty;
                    }
                }
            }

            return formatter.FormatCellValue(cell);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsLikelyEncrypted(Exception ex) =>
        ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
}
