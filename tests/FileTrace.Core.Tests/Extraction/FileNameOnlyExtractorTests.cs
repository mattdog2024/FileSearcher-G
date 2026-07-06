using FileTrace.Core.Extraction;

namespace FileTrace.Core.Tests.Extraction;

public class FileNameOnlyExtractorTests
{
    [Fact]
    public async Task ExtractAsync_AnyPath_ReturnsSuccessWithEmptyContent()
    {
        var extractor = new FileNameOnlyExtractor();
        // 故意传一个不存在的路径：FileNameOnlyExtractor 根本不应该触碰文件系统，
        // 只是策略性地跳过内容提取，因此即使文件不存在也应该"成功"返回空内容。
        var result = await extractor.ExtractAsync("/tmp/nonexistent-file-12345.ppt", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Content);
        Assert.Null(result.FailureReason);
    }

    [Theory]
    [InlineData("ppt")]
    [InlineData("pptx")]
    public void SupportedExtensions_MatchesPlanC(string extension)
    {
        var extractor = new FileNameOnlyExtractor();
        Assert.Contains(extension, extractor.SupportedExtensions);
    }
}
