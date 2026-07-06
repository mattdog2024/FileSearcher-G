using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;

namespace FileTrace.App.Services;

/// <summary>
/// 触发/控制一次索引任务（新建首次构建、增量更新、重建）的抽象，
/// 封装了 Core 层 <see cref="Core.Search.IndexingCoordinator"/> + <see cref="ManifestStore"/>
/// 的生命周期管理，UI 层只需要调用 <see cref="RunAsync"/> 并订阅进度回调。
///
/// Stage2 由 <see cref="Mock.MockIndexingService"/> 模拟一段带假进度的延时；
/// Stage3 由 <see cref="Real.RealIndexingService"/> 驱动真实的 FileSystemScanner + Lucene 写入。
/// </summary>
public interface IIndexingService
{
    /// <summary>
    /// 对指定索引执行一次扫描+索引写入任务。
    /// </summary>
    /// <param name="profile">要处理的索引配置。</param>
    /// <param name="rebuildFromScratch">true = 清空重建，false = 增量更新。</param>
    /// <param name="pauseController">供 UI"暂停/继续"按钮控制扫描进度。</param>
    /// <param name="progress">扫描进度回调，用于驱动索引卡片上的进度条/文案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IndexingSummary> RunAsync(
        IndexProfile profile,
        bool rebuildFromScratch,
        ScanPauseController pauseController,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default);
}
