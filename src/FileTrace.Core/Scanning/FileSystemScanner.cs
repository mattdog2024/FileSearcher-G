using FileTrace.Core.Extraction;
using FileTrace.Core.Models;

namespace FileTrace.Core.Scanning;

/// <summary>
/// 递归遍历一个 <see cref="IndexProfile.RootPath"/> 目录树，按用户勾选的扩展名过滤文件，
/// 结合 <see cref="ManifestStore"/> 做增量指纹判断，调用 <see cref="ExtractorRouter"/>
/// 提取内容，并以流式（IAsyncEnumerable）方式产出每个文件的处理结果。
///
/// 关键设计考量（面向"1TB级别硬盘、结果会持续增长"的场景）：
///   - 全程流式产出结果，不在内存里攒完整个文件列表再处理——避免百万级文件规模下的内存暴涨；
///   - 手动实现目录递归（而不是 Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)），
///     因为 .NET 的递归 EnumerateFiles 一旦在遍历过程中遇到某个子目录权限不足
///     （UnauthorizedAccessException）或路径过长，会直接让整个枚举中断抛出，
///     导致后面几十万个文件全部扫不到。这里逐层手动递归，权限/IO异常按目录单独捕获，
///     只跳过那一个子树，不影响兄弟目录继续扫描；
///   - 支持暂停（<see cref="ScanPauseController"/>）与取消（<see cref="CancellationToken"/>）；
///   - 增量模式下，命中"指纹未变化"的文件直接跳过重新提取，只在扫描结束后统一识别
///     manifest 中存在但本次未被访问到的路径为"已删除"。
/// </summary>
public sealed class FileSystemScanner
{
    private readonly ExtractorRouter _extractorRouter;

    public FileSystemScanner(ExtractorRouter? extractorRouter = null)
    {
        _extractorRouter = extractorRouter ?? new ExtractorRouter();
    }

    /// <summary>
    /// 执行一次扫描（全量或增量，取决于 <paramref name="profile"/>.IncrementalEnabled）。
    /// </summary>
    /// <param name="profile">索引配置：根目录、勾选的扩展名、增量开关、单文件大小上限等。</param>
    /// <param name="manifest">该索引对应的指纹清单库，用于增量判断与更新记录。</param>
    /// <param name="pauseController">暂停控制器，为 null 表示不支持暂停（例如单元测试场景）。</param>
    /// <param name="progress">扫描进度回调，用于驱动 UI 进度条/统计文案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async IAsyncEnumerable<ScanItemResult> ScanAsync(
        IndexProfile profile,
        ManifestStore manifest,
        ScanPauseController? pauseController,
        IProgress<ScanProgress>? progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var progressState = new ScanProgress();
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in EnumerateFilesRecursively(profile.RootPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            pauseController?.WaitIfPaused(cancellationToken);

            string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            if (!FileTypeCatalog.IsSelected(ext, profile.IncludedExtensions))
            {
                continue; // 用户没勾选这个类型，跳过（不计入 FilesScanned，避免进度数字包含用户不关心的文件）
            }

            visitedPaths.Add(filePath);

            ScanItemResult result = await ProcessSingleFileAsync(filePath, profile, manifest, cancellationToken)
                .ConfigureAwait(false);

            UpdateProgress(progressState, result);
            progress?.Report(progressState);

            yield return result;
        }

        // 扫描结束后，识别 manifest 里"上次记录过、但这次遍历完全没访问到"的路径——
        // 说明源文件已被删除、改名或移动，需要从索引里一并清除，否则会出现"搜到结果但文件已不存在"的体验问题。
        if (profile.IncrementalEnabled)
        {
            foreach (var removedPath in manifest.GetAllPaths())
            {
                if (visitedPaths.Contains(removedPath))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                progressState.FilesRemoved++;
                progress?.Report(progressState);

                yield return new ScanItemResult
                {
                    FullPath = removedPath,
                    Status = ScanItemStatus.Removed,
                };
            }
        }
    }

    private async Task<ScanItemResult> ProcessSingleFileAsync(
        string filePath,
        IndexProfile profile,
        ManifestStore manifest,
        CancellationToken cancellationToken)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return new ScanItemResult { FullPath = filePath, Status = ScanItemStatus.Failed, Reason = "扫描期间文件已消失" };
            }
        }
        catch (Exception ex)
        {
            return new ScanItemResult { FullPath = filePath, Status = ScanItemStatus.Failed, Reason = "无法读取文件属性: " + ex.Message };
        }

        long sizeBytes = fileInfo.Length;
        long mtimeTicks = fileInfo.LastWriteTimeUtc.Ticks;

        bool isNew = manifest.Find(filePath) is null;
        bool unchanged = profile.IncrementalEnabled && !isNew && manifest.IsUnchanged(filePath, sizeBytes, mtimeTicks);

        if (unchanged)
        {
            return new ScanItemResult { FullPath = filePath, Status = ScanItemStatus.Unchanged };
        }

        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var descriptor = FileTypeCatalog.Find(ext);
        bool tooLargeForContent = sizeBytes > profile.MaxFileSizeForContentBytes;

        string content = string.Empty;
        bool contentExtracted = false;
        string? skipOrFailureReason = null;

        if (descriptor is null)
        {
            skipOrFailureReason = "未知文件类型";
        }
        else if (descriptor.Strategy == ExtractionStrategy.FileNameOnly)
        {
            skipOrFailureReason = "该文件类型当前版本仅索引文件名（不提取全文内容）";
        }
        else if (tooLargeForContent)
        {
            skipOrFailureReason = $"文件大小超过 {profile.MaxFileSizeForContentBytes / 1024 / 1024}MB 上限，仅索引文件名";
        }
        else
        {
            try
            {
                ExtractResult extractResult = await _extractorRouter.ExtractAsync(filePath, cancellationToken)
                    .ConfigureAwait(false);
                if (extractResult.Success)
                {
                    content = extractResult.Content;
                    contentExtracted = true;
                }
                else
                {
                    skipOrFailureReason = extractResult.FailureReason ?? "内容提取失败（原因未知）";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 防御性兜底：理论上每个 IContentExtractor 内部都已经捕获了自己的异常，
                // 这里再兜一层，确保扫描器本身绝不会因为某个提取器的意外 bug 而整体崩溃。
                skipOrFailureReason = "提取器抛出未处理异常: " + ex.Message;
            }
        }

        var document = new ExtractedDocument
        {
            FullPath = filePath,
            FileName = fileInfo.Name,
            ExtensionNoDot = ext,
            SizeBytes = sizeBytes,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            Content = content,
            ContentExtracted = contentExtracted,
            SkipOrFailureReason = skipOrFailureReason,
        };

        manifest.Upsert(new FileFingerprint
        {
            FullPath = filePath,
            SizeBytes = sizeBytes,
            LastWriteTimeUtcTicks = mtimeTicks,
            IndexedAt = DateTimeOffset.UtcNow,
            ContentIndexed = contentExtracted,
        });

        return new ScanItemResult
        {
            FullPath = filePath,
            Status = isNew ? ScanItemStatus.Added : ScanItemStatus.Updated,
            Document = document,
            Reason = skipOrFailureReason,
        };
    }

    private static void UpdateProgress(ScanProgress state, ScanItemResult result)
    {
        state.CurrentPath = result.FullPath;

        switch (result.Status)
        {
            case ScanItemStatus.Added:
            case ScanItemStatus.Updated:
                state.FilesScanned++;
                state.FilesIndexed++;
                if (result.Document is not null)
                {
                    state.BytesProcessed += result.Document.SizeBytes;
                }
                break;
            case ScanItemStatus.Unchanged:
                state.FilesScanned++;
                state.FilesUnchanged++;
                break;
            case ScanItemStatus.Failed:
                state.FilesScanned++;
                state.FilesFailed++;
                break;
            case ScanItemStatus.Removed:
                // Removed 统计在扫描主循环结束后单独处理，这里不重复计入 FilesScanned
                break;
        }
    }

    /// <summary>
    /// 手动实现的递归文件枚举，逐层捕获目录级别的访问异常，保证一个坏子目录不会
    /// 拖垮整棵目录树的扫描。这是相对 <c>Directory.EnumerateFiles(root, "*", AllDirectories)</c>
    /// 的关键改进——后者在.NET里一旦中途抛出 UnauthorizedAccessException/PathTooLongException，
    /// 整个枚举会直接终止，而不是跳过那个子目录继续。
    /// </summary>
    private static IEnumerable<string> EnumerateFilesRecursively(string rootPath, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(rootPath);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDir = directories.Pop();

            IEnumerable<string> subDirectories = Array.Empty<string>();
            IEnumerable<string> files = Array.Empty<string>();

            try
            {
                subDirectories = Directory.EnumerateDirectories(currentDir).ToList();
                files = Directory.EnumerateFiles(currentDir).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                // 权限不足的目录（例如系统保护目录、回收站等），跳过整个子树，不影响其余部分
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                // 扫描期间目录被删除/是断开的符号链接目标，跳过
                continue;
            }
            catch (PathTooLongException)
            {
                // Windows 传统 API 260 字符路径长度限制，跳过这个子树
                continue;
            }
            catch (IOException)
            {
                // 目录不可读（例如硬盘正在弹出过程中）
                continue;
            }

            foreach (var dir in subDirectories)
            {
                directories.Push(dir);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
