using FileTrace.Core.Extraction;
using FileTrace.Core.Models;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Extraction;

public class ExtractorRouterTests
{
    [Fact]
    public void Constructor_DefaultExtractors_CoversEveryFileTypeCatalogEntry()
    {
        // 这是最重要的一致性回归测试：只要 FileTypeCatalog 新增了一个扩展名却忘记
        // 实现/注册对应的 IContentExtractor，这里就会在构造阶段直接失败，
        // 而不是等到运行时某个具体文件才暴露"不支持"的问题。
        var exception = Record.Exception(() => new ExtractorRouter());
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_DuplicateExtensionAcrossExtractors_ThrowsInvalidOperationException()
    {
        var extractors = new List<IContentExtractor>
        {
            new PlainTextExtractor(), // 声明支持 "txt"
            new FakeExtractor(new[] { "txt" }), // 也声明支持 "txt"，应冲突
        };

        Assert.Throws<InvalidOperationException>(() => new ExtractorRouter(extractors));
    }

    [Fact]
    public async Task ExtractAsync_UnknownExtension_ReturnsFailureWithoutThrowing()
    {
        var router = new ExtractorRouter();
        var result = await router.ExtractAsync("/tmp/foo.this-extension-does-not-exist", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("不支持的文件类型", result.FailureReason);
    }

    [Fact]
    public async Task ExtractAsync_PptExtension_ReturnsSuccessWithEmptyContent_PerPlanC()
    {
        // 方案C 回归测试：ppt/pptx 应该走 FileNameOnly 策略，返回成功但内容为空，
        // 而不是报错或抛异常。
        var router = new ExtractorRouter();
        var result = await router.ExtractAsync("/tmp/foo.ppt", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ExtractAsync_PptxExtension_ReturnsSuccessWithEmptyContent_PerPlanC()
    {
        var router = new ExtractorRouter();
        var result = await router.ExtractAsync("/tmp/foo.pptx", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Content);
    }

    [Fact]
    public async Task ExtractAsync_RoutesToCorrectExtractorByExtension()
    {
        using var dir = new TempDirectory();
        string txtPath = dir.CreateTextFile("test.txt", "plain text content 测试内容");

        var router = new ExtractorRouter();
        var result = await router.ExtractAsync(txtPath, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("plain text content 测试内容", result.Content);
    }

    [Theory]
    [InlineData("doc", ExtractionStrategy.FullText)]
    [InlineData("docx", ExtractionStrategy.FullText)]
    [InlineData("ppt", ExtractionStrategy.FileNameOnly)]
    [InlineData("pptx", ExtractionStrategy.FileNameOnly)]
    [InlineData("pdf", ExtractionStrategy.FullText)]
    public void GetStrategy_ReturnsExpectedStrategy(string extension, ExtractionStrategy expected)
    {
        var router = new ExtractorRouter();
        Assert.Equal(expected, router.GetStrategy(extension));
    }

    [Fact]
    public void GetStrategy_UnknownExtension_ReturnsNull()
    {
        var router = new ExtractorRouter();
        Assert.Null(router.GetStrategy("this-does-not-exist"));
    }

    [Fact]
    public void IsSupported_KnownAndUnknownExtensions()
    {
        var router = new ExtractorRouter();
        Assert.True(router.IsSupported("docx"));
        Assert.True(router.IsSupported(".docx")); // 兼容带点写法
        Assert.False(router.IsSupported("exe"));
    }

    private sealed class FakeExtractor : IContentExtractor
    {
        public FakeExtractor(IReadOnlyCollection<string> extensions) => SupportedExtensions = extensions;
        public IReadOnlyCollection<string> SupportedExtensions { get; }
        public Task<ExtractResult> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(ExtractResult.Ok(string.Empty));
    }
}
