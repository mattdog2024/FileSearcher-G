using System.IO;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;

namespace FileTrace.App.Services.Mock;

/// <summary>
/// Stage2 UI 骨架阶段使用的假索引任务：不触碰真实文件系统，用一段循环 + Task.Delay
/// 模拟"正在扫描 N 个文件"的进度回调节奏，驱动 IndexProfileCard 的进度条与文案联调。
/// </summary>
public sealed class MockIndexingService : IIndexingService
{
    public async Task<IndexingSummary> RunAsync(
        IndexProfile profile,
        bool rebuildFromScratch,
        ScanPauseController pauseController,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken = default)
    {
        const int fakeTotalFiles = 120;
        var state = new ScanProgress();

        for (int i = 1; i <= fakeTotalFiles; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pauseController.WaitIfPaused(cancellationToken);

            await Task.Delay(15, cancellationToken).ConfigureAwait(false);

            state.FilesScanned = i;
            state.FilesIndexed = i;
            state.CurrentPath = Path.Combine(profile.RootPath, $"示例文件_{i:D3}.docx");
            progress.Report(state);
        }

        return new IndexingSummary
        {
            Added = fakeTotalFiles,
            FinalDocumentCount = fakeTotalFiles,
        };
    }
}
