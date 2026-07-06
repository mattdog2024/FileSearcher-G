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

    public async Task<IndexingSummary> RunAsync(
        IndexProfile profile,
        bool rebuildFromScratch,
        ScanPauseController pauseController,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        using var manifest = ManifestStore.Open(profile.ManifestDbPath);

        return await _coordinator
            .RunAsync(profile, manifest, rebuildFromScratch, pauseController, progress, cancellationToken)
            .ConfigureAwait(false);
    }
}
