using FileTrace.Core.Models;
using FileTrace.Core.Scanning;

namespace FileTrace.Core.Search;

/// <summary>
/// 串联"文件系统扫描（<see cref="FileSystemScanner"/>）"与"Lucene 索引写入
/// （<see cref="IndexWriterService"/>）"的顶层协调器：把每个 <see cref="ScanItemResult"/>
/// 翻译成对应的索引写入动作（新增/更新/删除文档），并定期提交，是 UI 层发起"建立索引"/
/// "增量更新索引"操作时应该调用的入口。
///
/// 提交策略：每处理 <see cref="CommitBatchSize"/> 个"发生了实际索引写入"的文件就提交一次，
/// 而不是等全部扫描完成才提交——这样即使索引一个 1TB 级别的大盘中途异常中断/被用户暂停后关闭程序，
/// 已经处理完的部分不会丢失，重启后可以从 ManifestStore 记录的位置继续增量扫描。
/// </summary>
public sealed class IndexingCoordinator
{
    private const int CommitBatchSize = 500;

    private readonly FileSystemScanner _scanner;

    public IndexingCoordinator(FileSystemScanner? scanner = null)
    {
        _scanner = scanner ?? new FileSystemScanner();
    }

    /// <summary>
    /// 执行一次完整的索引任务：扫描 <paramref name="profile"/>.RootPath，
    /// 把结果同步写入 Lucene 索引与 manifest 清单库。
    /// </summary>
    /// <param name="profile">索引配置。</param>
    /// <param name="manifest">该索引对应的指纹清单库（调用方负责打开/释放）。</param>
    /// <param name="rebuildFromScratch">
    /// true 表示先清空索引再全量重建（对应 UI 上的"重建索引"按钮）；
    /// false 表示在已有索引基础上增量写入（对应"增量更新"，也是默认的定时/手动刷新行为）。
    /// </param>
    /// <param name="pauseController">暂停控制器，透传给底层 FileSystemScanner。</param>
    /// <param name="progress">进度回调，透传给底层 FileSystemScanner。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次索引任务的汇总统计。</returns>
    public async Task<IndexingSummary> RunAsync(
        IndexProfile profile,
        ManifestStore manifest,
        bool rebuildFromScratch,
        ScanPauseController? pauseController,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifest);

        using var writer = new IndexWriterService(profile.LuceneDirectory, profile.Id, createNew: rebuildFromScratch);

        var summary = new IndexingSummary();
        int uncommittedCount = 0;

        await foreach (var result in _scanner
            .ScanAsync(profile, manifest, pauseController, progress, cancellationToken)
            .ConfigureAwait(false))
        {
            switch (result.Status)
            {
                case ScanItemStatus.Added:
                    if (result.Document is not null)
                    {
                        writer.UpdateDocument(result.Document);
                        summary.Added++;
                        uncommittedCount++;
                    }
                    break;

                case ScanItemStatus.Updated:
                    if (result.Document is not null)
                    {
                        writer.UpdateDocument(result.Document);
                        summary.Updated++;
                        uncommittedCount++;
                    }
                    break;

                case ScanItemStatus.Removed:
                    writer.DeleteDocument(result.FullPath);
                    manifest.Remove(result.FullPath);
                    summary.Removed++;
                    uncommittedCount++;
                    break;

                case ScanItemStatus.Unchanged:
                    summary.Unchanged++;
                    break;

                case ScanItemStatus.Failed:
                    summary.Failed++;
                    break;
            }

            if (uncommittedCount >= CommitBatchSize)
            {
                writer.Commit();
                uncommittedCount = 0;
            }
        }

        // Dispose 时 IndexWriterService 会做最后一次 Commit，这里显式提前调用一次，
        // 确保方法返回前索引对搜索端立即可见（Dispose 发生在 using 块结束时，语义上等价，
        // 显式调用只是让"提交完成"这件事在日志/调用方视角上更明确）。
        writer.Commit();
        summary.FinalDocumentCount = writer.NumDocs;

        return summary;
    }
}

/// <summary>一次索引任务执行完成后的汇总统计，供 UI 展示"本次新增X个/更新X个/删除X个"。</summary>
public sealed class IndexingSummary
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }

    /// <summary>索引任务完成后，Lucene 索引中的文档总数。</summary>
    public int FinalDocumentCount { get; set; }
}
