using JiebaNet.Segmenter;

namespace FileTrace.Core.Search;

/// <summary>
/// 帮助 <see cref="FileTraceAnalyzer"/> 在 jieba.NET 词典首次加载之前，把
/// <see cref="ConfigManager.ConfigFileBaseDir"/> 显式设置到一个 **绝对路径**，
/// 从根上消除对 jieba.NET 默认行为（按相对路径 <c>Resources/</c>，基准是
/// <see cref="AppContext.BaseDirectory"/>）的依赖。
///
/// <para>
/// 为什么必须这样：在 .NET 8 单文件发布（<c>PublishSingleFile=true</c> +
/// <c>IncludeNativeLibrariesForSelfExtract=true</c>）场景下，运行时的
/// <see cref="AppContext.BaseDirectory"/> 指向 native 文件自解压到的临时目录
/// （<c>%LOCALAPPDATA%\Temp\.net\&lt;FileTrace&gt;\&lt;random&gt;\</c>），
/// 而不是 exe 自己所在的目录。如果让 jieba.NET 用默认的相对路径去找
/// <c>Resources/prob_emit.json</c> 等词典文件，它会从临时目录往下找，
/// 显然找不到，进而触发 <see cref="System.TypeInitializationException"/>
/// 抛出 <see cref="FileNotFoundException"/> 的 inner exception。
/// 此外 jieba.NET 的 <c>JiebaSegmenter..cctor()</c> 失败一次后会被 CLR 永久
/// 缓存，之后任何再次调用都会立刻重新抛出同类型异常（即使用
/// <see cref="ConfigManager.ConfigFileBaseDir"/> 重新设了正确路径也救不回来），
/// 所以正确的策略是「必须抢在第一次 <c>new JiebaSegmenter()</c> 之前设置好路径」。
/// </para>
///
/// <para>
/// 候选根目录查找顺序（先命中即返回）：
/// <list type="number">
/// <item>本目录：jieba.NET 程序集（<c>JiebaNet.Segmenter.dll</c>）所在文件夹下的
///       <c>Resources/</c>。这是便携版"把词典跟 jieba.dll 放在一起"时最自然的查找点。</item>
/// <item><see cref="AppContext.BaseDirectory"/> 下的 <c>Resources/</c>。覆盖
///       "jieba.dll 在 NuGet 缓存但 Resources 在 exe 旁边"的传统部署形态。</item>
/// </list>
/// 两条都没命中时抛 <see cref="DirectoryNotFoundException"/>，异常消息明确
/// 列出"期望的相对路径 <c>Resources/prob_emit.json</c>"，便于在 UI 层给出可读
/// 的修复建议（例如"请重新解压便携版，确保 exe 旁边的 Resources 文件夹完整"）。
/// </para>
/// </summary>
public static class JiebaResourceResolver
{
    /// <summary>
    /// jieba.NET 加载词典时会尝试读取的其中一个核心文件；只要它在某条候选目录下
    /// 存在，就能推断这一条路径就是 jieba.NET 想要查找的目录。
    /// </summary>
    public const string ProbeFileName = "prob_emit.json";

    private static readonly object SetupLock = new();
    private static bool _configured;
    private static string? _lastResolvedRoot;

    /// <summary>jieba.NET 词典所在的根目录绝对路径。只读，自上次 setup 后稳定。</summary>
    public static string? ResolvedRoot => _lastResolvedRoot;

    /// <summary>
    /// 找到并配置好 jieba.NET 词典目录，是幂等的。<see cref="JiebaSegmenter"/>
    /// 静态初始化 **失败一次后会永久缓存失败**（CLR 行为），所以调用必须抢在
    /// 任何 <c>new JiebaSegmenter()</c> 之前；本方法内部用锁 + 标志位双重保护，
    /// 并发调用也是安全的。
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// 所有候选路径下都找不到 <see cref="ProbeFileName"/> 探针文件时抛出，
    /// 异常消息明确列出已知的候选目录，便于定位是"便携版解压不完整"还是
    /// "Resources 部署位置与 csproj 不一致"。
    /// </exception>
    public static void EnsureConfigured()
    {
        if (_configured)
        {
            return;
        }

        lock (SetupLock)
        {
            if (_configured)
            {
                return;
            }

            string? resolved = TryResolve(out var checkedRoots);
            if (resolved is null)
            {
                throw new DirectoryNotFoundException(
                    "无法定位 jieba.NET 中文分词词典目录。请把" + ProbeFileName +
                    "以及同目录下的 dict.txt / idf.txt / prob_*.json / pos_prob_*.json / " +
                    "char_state_tab.json / stopwords.txt / cn_synonym.txt 全部放在程序可执行文件" +
                    "（FileTrace.exe）旁边的 Resources\\ 文件夹下后重新启动。已查找的候选目录：" +
                    string.Join(" ; ", checkedRoots));
            }

            ConfigManager.ConfigFileBaseDir = resolved;
            _lastResolvedRoot = resolved;
            _configured = true;
        }
    }

    /// <summary>
    /// 解析 jieba.NET 词典根目录的可单测纯函数：按顺序尝试所有候选目录，返回第一个
    /// 含 <see cref="ProbeFileName"/> 探针文件的根目录绝对路径；找不到返回 null，
    /// 并通过 <paramref name="checkedRoots"/> 输出所有尝试过的路径，便于排错。
    /// </summary>
    /// <param name="checkedRoots">已检查过的候选根目录（输出参数，便于错误信息中给出）。</param>
    /// <returns>第一个有效候选路径；找不到则返回 null。</returns>
    public static string? TryResolve(out IReadOnlyList<string> checkedRoots)
    {
        var tried = new List<string>();
        foreach (var candidate in EnumerateCandidateRoots())
        {
            string candidateFull = Path.GetFullPath(candidate);
            tried.Add(candidateFull);
            if (File.Exists(Path.Combine(candidateFull, ProbeFileName)))
            {
                checkedRoots = tried;
                return candidateFull;
            }
        }

        checkedRoots = tried;
        return null;
    }

    /// <summary>
    /// 仅测试用：覆盖默认的候选根目录枚举逻辑（返回一系列完整目录绝对路径，
    /// 不带 'Resources' 子串——本方法会自行拼接）。生产代码不应调用。
    /// </summary>
    internal static Func<IEnumerable<string>>? TestCandidateRootOverride { get; set; }

    /// <summary>
    /// 暴露给测试 / 诊断用途：枚举本进程内对 jieba.NET 词典的所有候选根目录。
    /// 候选顺序遵循 <see cref="TryResolve"/> 的约定。
    /// </summary>
    public static IEnumerable<string> EnumerateCandidateRoots()
    {
        if (TestCandidateRootOverride is { } overrideFn)
        {
            foreach (var root in overrideFn())
            {
                yield return root;
            }
            yield break;
        }

        // 候选 1：jieba.NET 程序集自身所在的目录。
        // netstandard2.0 dll 在 NuGet 包 layout 下通常位于
        // ~/.nuget/packages/jieba.net/0.42.2/lib/netstandard2.0/JiebaNet.Segmenter.dll，
        // Resources/ 与之并列；便携版/自定义部署时则与 exe 同目录。
        string? assemblyDir = Path.GetDirectoryName(typeof(JiebaSegmenter).Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            yield return Path.Combine(assemblyDir, "Resources");
        }

        // 候选 2：AppContext.BaseDirectory（exe 所在目录；在单文件发布场景下
        // 通常等于 dll 旁边目录，但 IDE 调试 / dotnet test 等特殊场景可能不同）。
        yield return Path.Combine(AppContext.BaseDirectory, "Resources");
    }

    /// <summary>
    /// 仅供测试使用：清除已缓存的解析结果，让下一次 <see cref="EnsureConfigured"/>
    /// 重新走解析流程。生产代码不应调用。
    /// </summary>
    internal static void ResetForTests()
    {
        lock (SetupLock)
        {
            _configured = false;
            _lastResolvedRoot = null;
        }
    }
}
