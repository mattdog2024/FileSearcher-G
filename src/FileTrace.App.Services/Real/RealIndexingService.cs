using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;

namespace FileTrace.App.Services.Real;

/// <summary>
/// <see cref="IIndexingService"/> 的真实实现：打开该索引对应的 <see cref="ManifestStore"/>，
/// 委托给 <see cref="IndexingCoordinator"/> 执行真实的文件系统扫描 + Lucene 索引写入，
/// 完成后正确释放 manifest 连接（IndexWriterService 内部已在 Coordinator 里用 using 管理）。
/// </summary>
public sealed class RealIndexingService : IIndexingService
{
    private readonly IndexingCoordinator _coordinator = new();

    public Task<IndexingSummary> RunAsync(
        IndexProfile profile,
        bool rebuildFromScratch,
        ScanPauseController pauseController,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        // 关键修复：目录递归遍历（Directory.EnumerateFiles/EnumerateDirectories）、
        // SQLite manifest 的同步查询/写入、暂停控制器 ManualResetEventSlim.Wait 阻塞等待，
        // 这些操作本质上全部是同步、CPU/IO 密集型调用。FileSystemScanner.ScanAsync 虽然
        // 签名上是 IAsyncEnumerable，但绝大多数分支（Unchanged 判定、ppt 仅文件名策略等）
        // 内部并不会产生真正会让出线程的 await 点——如果直接在调用方（UI 线程）上 await 整个
        // RunAsync，会导致 WPF 消息循环在整个索引任务运行期间被同步代码占满、无法处理重绘/
        // 输入事件，界面表现为完全卡死，索引卡片上的进度条/文案永远来不及渲染出来
        // （用户只能看到点击瞬间的"第一帧"画面）。
        //
        // 用 Task.Run 把整个索引任务显式切到线程池线程执行，UI 线程立刻返回、可以正常处理
        // 消息循环。Progress<ScanProgress> 在构造时已经在 UI 线程捕获了 SynchronizationContext，
        // 即使 Report() 是从线程池线程调用的，回调也会自动被封送（marshal）回 UI 线程执行，
        // 因此 MainViewModel 里更新 card.BuildProgressPercent/BuildProgressText 的逻辑不需要
        // 任何改动就能继续正确工作。
        return Task.Run(async () =>
        {
            using var manifest = ManifestStore.Open(profile.ManifestDbPath);

            return await _coordinator
                .RunAsync(profile, manifest, rebuildFromScratch, pauseController, progress, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);
    }
}
