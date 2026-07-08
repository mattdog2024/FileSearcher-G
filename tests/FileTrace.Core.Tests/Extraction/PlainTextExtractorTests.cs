using System.Text;
using FileTrace.Core.Extraction;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Extraction;

public class PlainTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Utf8File_ReturnsOriginalContent()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("test.txt", "Hello 世界，这是一个UTF-8测试文件。");

        var extractor = new PlainTextExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Hello 世界，这是一个UTF-8测试文件。", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_GbkEncodedFile_DecodesChineseCorrectly()
    {
        // 这是回归测试：验证 EncodingBootstrap 正确注册了 CodePages 提供程序，
        // GBK 编码的中文老文件不会被误判成乱码。此前发现过这个 bug（默认.NET不带GBK支持）。
        using var dir = new TempDirectory();
        byte[] gbkBytes = Encoding.GetEncoding("GBK").GetBytes("这是GBK编码的中文测试内容");
        string path = dir.CreateBinaryFile("gbk_test.txt", gbkBytes);

        var extractor = new PlainTextExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("这是GBK编码的中文测试内容", result.Content);
    }

    [Fact]
    public async Task ExtractAsync_EmptyFile_ReturnsSuccessWithEmptyContent()
    {
        using var dir = new TempDirectory();
        string path = dir.CreateTextFile("empty.txt", string.Empty);

        var extractor = new PlainTextExtractor();
        var result = await extractor.ExtractAsync(path, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ExtractAsync_NonExistentFile_ReturnsFailureWithoutThrowing()
    {
        var extractor = new PlainTextExtractor();
        var result = await extractor.ExtractAsync("/tmp/this-file-does-not-exist-12345.txt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("md")]
    [InlineData("log")]
    [InlineData("json")]
    [InlineData("py")]
    [InlineData("csv")]
    public void SupportedExtensions_ContainsExpectedTypes(string extension)
    {
        var extractor = new PlainTextExtractor();
        Assert.Contains(extension, extractor.SupportedExtensions);
    }
}
