using System.IO.Compression;
using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Extraction;

public class EpubExtractorTests
{
    /// <summary>构造一个符合标准 EPUB 结构（container.xml + OPF manifest/spine）的最小测试文件。</summary>
    private static string CreateStandardEpub(TempDirectory dir, string fileName = "test.epub")
    {
        string epubPath = System.IO.Path.Combine(dir.Path, fileName);
        using (var fs = new FileStream(epubPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "mimetype", "application/epub+zip");
            WriteEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);
            WriteEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
                  <manifest>
                    <item id="ch1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="ch2" href="chapter2.xhtml" media-type="application/xhtml+xml"/>
                  </manifest>
                  <spine>
                    <itemref idref="ch1"/>
                    <itemref idref="ch2"/>
                  </spine>
                </package>
                """);
            WriteEntry(archive, "OEBPS/chapter1.xhtml", "<html><body><h1>第一章</h1><p>这是中文测试内容，第一章正文。</p></body></html>");
            WriteEntry(archive, "OEBPS/chapter2.xhtml", "<html><body><h1>第二章</h1><p>Second chapter content here.</p></body></html>");
        }
        return epubPath;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
        writer.Write(content);
    }

    [Fact]
    public async Task ExtractAsync_StandardEpub_ExtractsChaptersInSpineOrder()
    {
        using var dir = new TempDirectory();
        string path = CreateStandardEpub(dir);

        var extractor = new EpubExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("这是中文测试内容，第一章正文。", result.Content);
        Assert.Contains("Second chapter content here.", result.Content);

        // 验证 spine 顺序：第一章内容应出现在第二章之前
        int idxChapter1 = result.Content.IndexOf("第一章正文", StringComparison.Ordinal);
        int idxChapter2 = result.Content.IndexOf("Second chapter", StringComparison.Ordinal);
        Assert.True(idxChapter1 < idxChapter2, "章节内容应按 spine 声明的阅读顺序拼接");
    }

    [Fact]
    public async Task ExtractAsync_EpubWithoutContainerXml_FallsBackToScanningHtmlEntries()
    {
        using var dir = new TempDirectory();
        string epubPath = System.IO.Path.Combine(dir.Path, "malformed.epub");
        using (var fs = new FileStream(epubPath, FileMode.Create))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // 故意不写 META-INF/container.xml，模拟损坏/非标准 EPUB，验证兜底遍历模式生效
            WriteEntry(archive, "content.html", "<html><body><p>Fallback scan content 兜底扫描内容</p></body></html>");
        }

        var extractor = new EpubExtractor();
        var result = await extractor.ExtractAsync(epubPath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Fallback scan content 兜底扫描内容", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_NotAZipFile_ReturnsFailureWithoutThrowing()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("fake.epub", "this is not a zip file at all");

        var extractor = new EpubExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ExtractAsync_HtmlTagsAreStrippedFromOutput()
    {
        using var dir = new TempDirectory();
        string path = CreateStandardEpub(dir);

        var extractor = new EpubExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("<h1>", result.Content);
        Assert.DoesNotContain("<p>", result.Content);
    }
}
