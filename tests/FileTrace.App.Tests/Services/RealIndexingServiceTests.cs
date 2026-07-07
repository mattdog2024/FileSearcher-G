using System.Diagnostics;
using FileTrace.App.Services.Real;
using FileTrace.App.Tests.TestHelpers;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;
using Xunit;

namespace FileTrace.App.Tests.Services;

public class RealIndexingServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IndexProfile CreateProfile(string rootPath, string storagePath) => new()
    {
        Name = "集成测试索引",
        RootPath = rootPath,
        StoragePath = storagePath,
        IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "txt" },
        IncrementalEnabled = true,
    };

    [Fact]
    public async Task RunAsync_FreshRootDirectory_WritesSearchableLuceneIndex()
    {
        string rootPath = Path.Combine(_temp.Path, "root");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, "a.txt"), "寻迹文件搜索软件测试内容");
        string storagePath = Path.Combine(_temp.Path, "storage");

        var profile = CreateProfile(rootPath, storagePath);
        var service = new RealIndexingService();
        var progressReports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => progressReports.Add(p));

        var summary = await service.RunAsync(
            profile, rebuildFromScratch: true, new ScanPauseController(), progress);

        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.FinalDocumentCount);
        Assert.True(Directory.Exists(profile.LuceneDirectory));
        Assert.True(File.Exists(profile.ManifestDbPath));

        // 验证真的可以通过 SearchService 搜到刚写入的内容——端到端确认 RealIndexingService
        // 与 Search 端使用的是同一套可互相理解的 Lucene 索引格式/字段。
        using var search = new FileTrace.Core.Search.SearchService(profile.LuceneDirectory);
        var result = search.Search("寻迹");
        Assert.Equal(1, result.TotalHits);
    }

    [Fact]
    public async Task RunAsync_CalledTwiceIncrementally_SecondRunReportsUnchanged()
    {
        string rootPath = Path.Combine(_temp.Path, "root2");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, "b.txt"), "不变的内容");
        string storagePath = Path.Combine(_temp.Path, "storage2");

        var profile = CreateProfile(rootPath, storagePath);
        var service = new RealIndexingService();

        var firstRun = await service.RunAsync(profile, rebuildFromScratch: true, new ScanPauseController(), null);
        Assert.Equal(1, firstRun.Added);

        var secondRun = await service.RunAsync(profile, rebuildFromScratch: false, new ScanPauseController(), null);
        Assert.Equal(0, secondRun.Added);
        Assert.Equal(1, secondRun.Unchanged);
    }

    [Fact]
    public async Task RunAsync_ReturnsTaskImmediately_DoesNotBlockCallerUntilScanCompletes()
    {
        // 回归测试：修复前 RealIndexingService.RunAsync 是一个直接在调用方线程上同步
        // 执行整个扫描流程的 async 方法——C# 的 async 方法在没有遇到"真正会挂起的
        // await 点"之前，只会在调用方线程上原地同步跑完，不会提前把控制权交还调用方。
        // 这正是真实用户反馈"点击重建索引后界面立刻卡死、进度条/文案来不及渲染"的根因
        // （对应场景：选中一整块硬盘 E:\ 作为索引根目录，目录下有大量文件）。
        //
        // 这里刻意构造一个"所有文件的扩展名都不在勾选范围内，因此整个扫描过程里
        // 不存在任何真正的异步挂起点"的场景——绝大多数文件在扩展名过滤阶段就被
        // 同步 continue 跳过，完全不会调用任何提取器：
        //   - 修复前：调用 RunAsync(...) 本身（先不 await，只拿返回的 Task 引用）
        //     必须等整个目录遍历完成才能返回，"拿到 Task"和"扫描全部完成"几乎同时发生。
        //   - 修复后：Task.Run 会立即把工作派发到线程池的另一个线程上执行，
        //     调用方几乎瞬间（远早于扫描完成）就能拿到一个"尚未完成"的 Task。
        //
        // 用相对比例（拿到 Task 的耗时 应显著小于 总耗时）而非固定毫秒阈值断言，
        // 避免测试因运行环境快慢不同而不稳定：文件数量足够多时，"同步执行完整个
        // 扫描"与"仅排队到线程池"之间的耗时差距会被放大到不会被计时误差掩盖的程度。
        string rootPath = Path.Combine(_temp.Path, "root_block");
        Directory.CreateDirectory(rootPath);
        const int fileCount = 20_000;
        for (int i = 0; i < fileCount; i++)
        {
            // .dat 不在下面 CreateProfile 勾选的 "txt" 范围内，扫描时会在扩展名过滤阶段
            // 被同步 continue 跳过，完全不会调用任何提取器 / 不会产生真正的异步挂起点。
            File.WriteAllText(Path.Combine(rootPath, $"file_{i}.dat"), "x");
        }
        string storagePath = Path.Combine(_temp.Path, "storage_block");

        var profile = CreateProfile(rootPath, storagePath);
        var service = new RealIndexingService();

        var sw = Stopwatch.StartNew();
        Task<IndexingSummary> runTask = service.RunAsync(profile, rebuildFromScratch: true, new ScanPauseController(), null);
        var elapsedToObtainTask = sw.Elapsed;

        var summary = await runTask;
        var elapsedToCompletion = sw.Elapsed;

        Assert.Equal(0, summary.Added); // 全部因扩展名未勾选被过滤，没有文件实际被索引

        // 修复后拿到 Task 引用的耗时应当只是总扫描耗时的一小部分（这里放宽到 50% 阈值，
        // 兼顾 CI 环境抖动）。如果 RunAsync 仍在调用方线程上同步阻塞执行整个扫描，
        // 这个比例会接近 100%（两者几乎同时发生）。
        double ratio = elapsedToCompletion.TotalMilliseconds > 0
            ? elapsedToObtainTask.TotalMilliseconds / elapsedToCompletion.TotalMilliseconds
            : 0;
        Assert.True(ratio < 0.5,
            $"调用 RunAsync() 后拿到 Task 的耗时（{elapsedToObtainTask.TotalMilliseconds:N0}ms）应当远小于" +
            $"总扫描耗时（{elapsedToCompletion.TotalMilliseconds:N0}ms，占比 {ratio:P0}）。" +
            "如果占比接近 100%，说明扫描逻辑仍然在调用方线程上同步阻塞执行，会导致 WPF UI 线程" +
            "在索引任务运行期间完全无法响应（界面卡死，进度条/文案来不及渲染）。");
    }

    [Fact]
    public async Task RunAsync_RebuildFromScratch_ClearsPreviouslyRemovedFileFromIndex()
    {
        string rootPath = Path.Combine(_temp.Path, "root3");
        Directory.CreateDirectory(rootPath);
        string filePath = Path.Combine(rootPath, "c.txt");
        File.WriteAllText(filePath, "即将被删除的文件内容");
        string storagePath = Path.Combine(_temp.Path, "storage3");

        var profile = CreateProfile(rootPath, storagePath);
        var service = new RealIndexingService();

        await service.RunAsync(profile, rebuildFromScratch: true, new ScanPauseController(), null);

        File.Delete(filePath);
        var secondRun = await service.RunAsync(profile, rebuildFromScratch: false, new ScanPauseController(), null);

        Assert.Equal(1, secondRun.Removed);
        Assert.Equal(0, secondRun.FinalDocumentCount);
    }
}
