using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Extraction;

public class PdfExtractorTests
{
    [Fact]
    public async Task ExtractAsync_NonExistentFile_ReturnsFailureWithoutThrowing()
    {
        var extractor = new PdfExtractor();
        var result = await extractor.ExtractAsync("/tmp/does-not-exist-12345.pdf", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public async Task ExtractAsync_CorruptedFile_ReturnsFailureWithoutThrowing()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("corrupted.pdf", "this is not a valid PDF file at all, just plain text");

        var extractor = new PdfExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void SupportedExtensions_ContainsPdf()
    {
        var extractor = new PdfExtractor();
        Assert.Contains("pdf", extractor.SupportedExtensions);
    }

    [Fact]
    public async Task ExtractAsync_ValidPdfFixture_ExtractsPageText()
    {
        string fixturePath = System.IO.Path.Combine(AppContext.BaseDirectory, "TestFixtures", "sample.pdf");
        Assert.True(File.Exists(fixturePath), $"测试固定素材缺失: {fixturePath}");

        var extractor = new PdfExtractor();
        var result = await extractor.ExtractAsync(fixturePath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Hello PdfPig Test Content", result.Content);
    }
}
