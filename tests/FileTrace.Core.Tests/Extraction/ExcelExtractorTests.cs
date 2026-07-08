using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace FileTrace.Core.Tests.Extraction;

public class ExcelExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Xlsx_ExtractsCellText()
    {
        using var dir = new TempDirectory();
        string path = System.IO.Path.Combine(dir.Path, "test.xlsx");

        IWorkbook? wb = null;
        try
        {
            wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Sheet1");
            var row0 = sheet.CreateRow(0);
            row0.CreateCell(0).SetCellValue("产品名称");
            row0.CreateCell(1).SetCellValue("销售额");
            var row1 = sheet.CreateRow(1);
            row1.CreateCell(0).SetCellValue("华东区季度报告");
            row1.CreateCell(1).SetCellValue(12345.67);
            using var fs = new FileStream(path, FileMode.Create);
            wb.Write(fs);
        }
        finally
        {
            wb?.Close();
        }

        var extractor = new ExcelExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("产品名称", result.Content);
        Assert.Contains("华东区季度报告", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_XlsxWithFormula_EvaluatesFormulaValue()
    {
        using var dir = new TempDirectory();
        string path = System.IO.Path.Combine(dir.Path, "formula.xlsx");

        IWorkbook? wb = null;
        try
        {
            wb = new XSSFWorkbook();
            var sheet = wb.CreateSheet("Sheet1");
            var row = sheet.CreateRow(0);
            row.CreateCell(0).SetCellValue(10);
            row.CreateCell(1).SetCellValue(20);
            row.CreateCell(2).SetCellFormula("A1+B1");
            using var fs = new FileStream(path, FileMode.Create);
            wb.Write(fs);
        }
        finally
        {
            wb?.Close();
        }

        var extractor = new ExcelExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("30", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_LegacyXls_ExtractsCellText()
    {
        using var dir = new TempDirectory();
        string path = System.IO.Path.Combine(dir.Path, "legacy.xls");

        HSSFWorkbook? wb = null;
        try
        {
            wb = new HSSFWorkbook();
            var sheet = wb.CreateSheet("Sheet1");
            var row = sheet.CreateRow(0);
            row.CreateCell(0).SetCellValue("老版 xls 测试：季度销售报表");
            using var fs = new FileStream(path, FileMode.Create);
            wb.Write(fs);
        }
        finally
        {
            wb?.Close();
        }

        var extractor = new ExcelExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("老版 xls 测试：季度销售报表", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_CorruptedFile_ReturnsFailureWithoutThrowing()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("corrupted.xlsx", "not a valid xlsx file");

        var extractor = new ExcelExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }
}
