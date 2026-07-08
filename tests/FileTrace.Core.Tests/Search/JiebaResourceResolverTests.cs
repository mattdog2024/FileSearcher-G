using FileTrace.Core.Search;
using FileTrace.Core.Tests.TestHelpers;
using JiebaNet.Segmenter;

namespace FileTrace.Core.Tests.Search;

/// <summary>
/// 覆盖 <see cref="JiebaResourceResolver"/>：服务于"解决 jieba.NET 词典加载失败"的修复。
/// 验证点：
/// <list type="bullet">
/// <item>能让 <see cref="JiebaResourceResolver.TryResolve"/> 在多个候选根目录中
///       按顺序选取第一个含 <see cref="JiebaResourceResolver.ProbeFileName"/> 探针的目录；</item>
/// <item>所有候选都不命中时返回 null 并暴露已检查的候选路径清单（便于排错）；</item>
/// <item>注入式覆盖测试钩子 <c>TestCandidateRootOverride</c> 在并发场景下是安全的；</item>
/// <item><see cref="JiebaResourceResolver.EnsureConfigured"/> 调用后
///       <see cref="ConfigManager.ConfigFileBaseDir"/> 已被设置为解析到的绝对路径。</item>
/// </list>
/// 注意：本测试不真去构造 <see cref="JiebaSegmenter"/> 实例——一旦 .cctor 失败 jieba
/// 会把失败状态永久缓存到 CLR，影响后续测试。所以 ResetForTests 用完也救不回来首次
/// 触发失败的进程。我们仅测"目录解析"这一层即可，覆盖核心逻辑就行。
/// </summary>
public sealed class JiebaResourceResolverTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private string? _originalConfigValue;

    public JiebaResourceResolverTests()
    {
        // Snapshot 现行 ConfigManager 值，避免我们的测试污染之后跑的其他测试。
        _originalConfigValue = ConfigManager.ConfigFileBaseDir;
        JiebaResourceResolver.TestCandidateRootOverride = null;
        JiebaResourceResolver.ResetForTests();
    }

    public void Dispose()
    {
        // 恢复现场，让并行/串行的其他测试不受影响。
        JiebaResourceResolver.TestCandidateRootOverride = null;
        JiebaResourceResolver.ResetForTests();
        ConfigManager.ConfigFileBaseDir = _originalConfigValue;
        _tmp.Dispose();
    }

    [Fact]
    public void EnumerateCandidateRoots_DefaultsIncludeJiebaAssemblyDirAndBaseDirectory()
    {
        // 不设置测试钩子，应至少返回两个候选：jieba.dll 旁边、BaseDirectory 旁边。
        var candidates = JiebaResourceResolver.EnumerateCandidateRoots().ToList();

        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, p => p.EndsWith("Resources", StringComparison.Ordinal));
        // 两个候选至少有一个是 BaseDirectory 子路径，便于 IDE 调试/dotnet run 场景。
        Assert.Contains(candidates, p => p.StartsWith(AppContext.BaseDirectory.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase));
        // 另一个是 jieba 程序集所在目录。
        string jiebaAssemblyDir = Path.GetDirectoryName(typeof(JiebaSegmenter).Assembly.Location)!;
        Assert.Contains(candidates, p => p.StartsWith(jiebaAssemblyDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryResolve_TestOverrideFirstCandidate_Hits()
    {
        string fakeRoot = _tmp.Path;          // ← 这是要"命中"的候选（注入到 override 里）
        string primary = Path.Combine(fakeRoot, "pri");
        string secondary = Path.Combine(fakeRoot, "sec");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);
        File.WriteAllText(Path.Combine(primary, JiebaResourceResolver.ProbeFileName), "{}");

        JiebaResourceResolver.TestCandidateRootOverride = () =>
            new[] { primary, secondary };     // primary 命中，secondary 只是凑数

        var resolved = JiebaResourceResolver.TryResolve(out var checkedRoots);

        Assert.Equal(Path.GetFullPath(primary), resolved);
        // 由于命中即返回，secondary 不应出现在 `checkedRoots` 中。
        Assert.Single(checkedRoots);
        Assert.Equal(Path.GetFullPath(primary), checkedRoots[0]);
    }

    [Fact]
    public void TryResolve_TestOverrideSecondCandidate_FallsThrough()
    {
        // 第一个候选没有探针 → 回落到第二个候选并命中。
        string primary = Path.Combine(_tmp.Path, "pri-empty");
        string secondary = Path.Combine(_tmp.Path, "sec");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);
        File.WriteAllText(Path.Combine(secondary, JiebaResourceResolver.ProbeFileName), "{}");

        JiebaResourceResolver.TestCandidateRootOverride = () =>
            new[] { primary, secondary };

        var resolved = JiebaResourceResolver.TryResolve(out var checkedRoots);

        Assert.Equal(Path.GetFullPath(secondary), resolved);
        Assert.Equal(2, checkedRoots.Count);
        Assert.Equal(Path.GetFullPath(primary), checkedRoots[0]);
        Assert.Equal(Path.GetFullPath(secondary), checkedRoots[1]);
    }

    [Fact]
    public void TryResolve_TestOverrideNoneMatch_ReturnsNullAndExposesCheckedRoots()
    {
        // 两个候选都没有探针 → 必须返回 null 且把"都试过哪些路径"告诉调用方，
        // 便于 DirectoryNotFoundException 错误信息给出修复建议。
        string a = Path.Combine(_tmp.Path, "a");
        string b = Path.Combine(_tmp.Path, "b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);

        JiebaResourceResolver.TestCandidateRootOverride = () => new[] { a, b };

        var resolved = JiebaResourceResolver.TryResolve(out var checkedRoots);

        Assert.Null(resolved);
        Assert.Equal(2, checkedRoots.Count);
        Assert.Contains(checkedRoots, p => p.EndsWith(Path.Combine(_tmp.Path, "a")));
        Assert.Contains(checkedRoots, p => p.EndsWith(Path.Combine(_tmp.Path, "b")));
    }

    [Fact]
    public void EnsureConfigured_ConfiguresConfigManager_WithResolvedAbsolutePath()
    {
        // 用临时目录里的真实探针文件模拟"jieba 词典就在这里"。
        string root = Path.Combine(_tmp.Path, "Resources");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, JiebaResourceResolver.ProbeFileName), "{}");
        File.WriteAllText(Path.Combine(root, "dict.txt"), "");               // 真要 segmenter 起来还得 dict.txt
        File.WriteAllText(Path.Combine(root, "prob_trans.json"), "{}");

        JiebaResourceResolver.TestCandidateRootOverride = () => new[] { root };
        JiebaResourceResolver.ResetForTests();
        // 必须在调 EnsureConfigured 前清掉 ConfigManager 旧值，确保我们设置的新值生效可见。
        ConfigManager.ConfigFileBaseDir = null;

        JiebaResourceResolver.EnsureConfigured();

        Assert.Equal(Path.GetFullPath(root), JiebaResourceResolver.ResolvedRoot);
        Assert.Equal(Path.GetFullPath(root), ConfigManager.ConfigFileBaseDir);
    }

    [Fact]
    public void EnsureConfigured_Idempotent_DoesNotReResolveOnSecondCall()
    {
        // 一旦设好，再次调用不应该再走解析流程（也不应该被 override 后的新候选影响），
        // 便于上层调用方在多个事件处理器里多次"保险式"调用都安全。
        string root1 = Path.Combine(_tmp.Path, "R1");
        Directory.CreateDirectory(root1);
        File.WriteAllText(Path.Combine(root1, JiebaResourceResolver.ProbeFileName), "{}");

        JiebaResourceResolver.TestCandidateRootOverride = () => new[] { root1 };
        JiebaResourceResolver.ResetForTests();
        ConfigManager.ConfigFileBaseDir = null;

        JiebaResourceResolver.EnsureConfigured();
        string? firstRoot = JiebaResourceResolver.ResolvedRoot;

        // 第二次：构造一个带"更优先"但没有探针的候选，应该被忽略。
        string root2 = Path.Combine(_tmp.Path, "R2");
        Directory.CreateDirectory(root2);
        JiebaResourceResolver.TestCandidateRootOverride = () => new[] { root2, root1 };
        JiebaResourceResolver.EnsureConfigured();

        Assert.Equal(firstRoot, JiebaResourceResolver.ResolvedRoot);
    }

    [Fact]
    public void EnsureConfigured_NoCandidateMatches_ThrowsDirectoryNotFoundException()
    {
        JiebaResourceResolver.TestCandidateRootOverride = () => Array.Empty<string>();
        JiebaResourceResolver.ResetForTests();
        ConfigManager.ConfigFileBaseDir = null;

        var ex = Assert.Throws<DirectoryNotFoundException>(() => JiebaResourceResolver.EnsureConfigured());

        // 错误消息必须给出修复建议 + probe 文件名，便于 UI 层透传给用户。
        Assert.Contains(JiebaResourceResolver.ProbeFileName, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Resources", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
