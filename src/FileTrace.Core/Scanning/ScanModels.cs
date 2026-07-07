namespace FileTrace.Core.Scanning;

/// <summary>
/// 单个文件在本次扫描中的处理结果分类。
/// </summary>
public enum ScanItemStatus
{
    /// <summary>manifest 中不存在，属于新发现的文件，已（尝试）提取内容。</summary>
    Added,

    /// <summary>manifest 中已存在但大小/修改时间发生变化，已重新提取内容。</summary>
    Updated,

    /// <summary>增量模式下判定指纹未变化，跳过重新提取，索引中该文件的记录保持不变。</summary>
    Unchanged,

    /// <summary>manifest 中存在但本次扫描未再发现该路径（文件被删除/移动/源盘不完整可见），需要从索引中移除。</summary>
    Removed,

    /// <summary>访问文件/目录时发生错误（权限不足、路径过长、IO异常等），已跳过。</summary>
    Failed,
}

/// <summary>
/// 单个文件的扫描结果。<see cref="Models.ExtractedDocument"/> 仅在 Status 为
/// Added/Updated 时非空——这是需要写入/更新 Lucene 索引的记录。
/// </summary>
public sealed class ScanItemResult
{
    public required string FullPath { get; init; }
    public required ScanItemStatus Status { get; init; }
    public Models.ExtractedDocument? Document { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// 扫描过程的实时统计信息，通过 <see cref="IProgress{T}"/> 回调给 UI 层，
/// 对应设计稿里索引构建进度条 + "已处理 X / 共发现 Y 个文件"这类文案。
/// </summary>
public sealed class ScanProgress
{
    /// <summary>已扫描并匹配了勾选类型的文件总数（不含被类型过滤掉的文件）。</summary>
    public long FilesScanned { get; set; }

    /// <summary>其中新增或更新、执行了实际提取的文件数。</summary>
    public long FilesIndexed { get; set; }

    /// <summary>增量模式下判定未变化、跳过重新提取的文件数。</summary>
    public long FilesUnchanged { get; set; }

    /// <summary>提取失败或访问异常的文件数。</summary>
    public long FilesFailed { get; set; }

    /// <summary>已确认被删除、待从索引移除的文件数。</summary>
    public long FilesRemoved { get; set; }

    /// <summary>已处理文件的累计字节数（用于估算速度/剩余时间）。</summary>
    public long BytesProcessed { get; set; }

    /// <summary>当前正在处理的文件路径，用于 UI 展示"正在索引: xxx.docx"。</summary>
    public string? CurrentPath { get; set; }
}

/// <summary>
/// 扫描任务的暂停/恢复控制器。设计稿里索引构建卡片上有"暂停"按钮，
/// 用户暂停后扫描线程应该阻塞在当前文件处理完成的边界上，而不是立即中断
/// （避免半途写入不完整的提取结果）。恢复时从下一个文件继续，不丢失已完成的进度。
///
/// 与 CancellationToken 的区别：Cancel 是"停止并放弃"，Pause 是"暂时挂起，之后可以继续"。
/// </summary>
public sealed class ScanPauseController
{
    private readonly ManualResetEventSlim _resumeGate = new(initialState: true);
    private readonly System.Diagnostics.Stopwatch _currentPauseStopwatch = new();
    private TimeSpan _accumulatedPauseDuration = TimeSpan.Zero;

    public bool IsPaused { get; private set; }

    /// <summary>
    /// 自任务开始以来累计的"暂停时长"（不含当前若仍处于暂停中的这一段未结束的时间也会被计入，
    /// 见下方 getter 实现）。用于 UI 层计算"已用时"时把暂停期间排除掉——暂停时用户可能长时间
    /// 不操作，如果把这段时间也计入"已用时"，会导致"处理速度""预计剩余时间"被严重低估/夸大。
    /// </summary>
    public TimeSpan TotalPausedDuration =>
        _accumulatedPauseDuration + (_currentPauseStopwatch.IsRunning ? _currentPauseStopwatch.Elapsed : TimeSpan.Zero);

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        _currentPauseStopwatch.Restart();
        _resumeGate.Reset();
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        _currentPauseStopwatch.Stop();
        _accumulatedPauseDuration += _currentPauseStopwatch.Elapsed;
        _resumeGate.Set();
    }

    /// <summary>
    /// 扫描循环在处理每个文件之前调用一次：如果当前处于暂停状态就阻塞在这里，
    /// 直到 <see cref="Resume"/> 被调用或 <paramref name="cancellationToken"/> 被取消。
    /// </summary>
    public void WaitIfPaused(CancellationToken cancellationToken)
    {
        _resumeGate.Wait(cancellationToken);
    }

    /// <summary>
    /// 清空累计暂停时长与暂停状态，供每次发起新的索引任务（新建/重建）前调用——
    /// 同一张索引卡片在其生命周期内只持有一个 ScanPauseController 实例（会被复用于
    /// 多次"重建索引"操作），如果不重置，上一次构建遗留的暂停时长会污染这一次的
    /// "已用时/预计剩余时间"计算。
    /// </summary>
    public void Reset()
    {
        IsPaused = false;
        _currentPauseStopwatch.Reset();
        _accumulatedPauseDuration = TimeSpan.Zero;
        _resumeGate.Set();
    }
}
