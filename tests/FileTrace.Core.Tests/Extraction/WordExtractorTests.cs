using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;
using NPOI.HWPF;
using NPOI.XWPF.UserModel;

namespace FileTrace.Core.Tests.Extraction;

public class WordExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Docx_ExtractsParagraphText()
    {
        using var dir = new TempDirectory();
        string path = System.IO.Path.Combine(dir.Path, "test.docx");

        XWPFDocument? doc = null;
        try
        {
            doc = new XWPFDocument();
            var paragraph = doc.CreateParagraph();
            var run = paragraph.CreateRun();
            run.SetText("这是一个中文测试文档，验证 docx 段落内容提取。");
            using var fs = new FileStream(path, FileMode.Create);
            doc.Write(fs);
        }
        finally
        {
            doc?.Close();
        }

        var extractor = new WordExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("这是一个中文测试文档，验证 docx 段落内容提取。", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_DocxWithTable_ExtractsTableCellText()
    {
        using var dir = new TempDirectory();
        string path = System.IO.Path.Combine(dir.Path, "table.docx");

        XWPFDocument? doc = null;
        try
        {
            doc = new XWPFDocument();
            var table = doc.CreateTable(1, 2);
            table.GetRow(0).GetCell(0).SetText("产品名称");
            table.GetRow(0).GetCell(1).SetText("华东区季度报告");
            using var fs = new FileStream(path, FileMode.Create);
            doc.Write(fs);
        }
        finally
        {
            doc?.Close();
        }

        var extractor = new WordExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("产品名称", result.Content);
        Assert.Contains("华东区季度报告", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_Doc_ExtractsTextFromLegacyBinaryFormat()
    {
        // 注意：NPOI 2.5.6 的 HWPFDocument() 无参构造函数生成的内部结构不完整
        // （FIB/文本表等字段未正确初始化），用它“从零构造再写回”的 .doc 文件重新打开时
        // 会在 Range.Text 上抛 ArgumentOutOfRangeException —— 这是 NPOI 该版本的已知缺陷，
        // 并非本项目提取逻辑的问题。因此这里改用真实 Word 97 二进制 fixture 文件
        // （由 LibreOffice 生成，与生产环境中用户实际拥有的老 .doc 文件同源），
        // 只验证“读取真实 .doc”这条生产路径。
        string fixturePath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "TestFixtures", "sample.doc");
        Assert.True(File.Exists(fixturePath), $"测试固定素材不存在: {fixturePath}");

        var extractor = new WordExtractor();
        var result = await extractor.ExtractAsync(fixturePath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("这是老版 .doc (Word 97-2003) 格式测试文本。", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_CorruptedFile_ReturnsFailureWithoutThrowing()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("corrupted.docx", "this is not a valid docx/zip file at all");

        var extractor = new WordExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }
}
