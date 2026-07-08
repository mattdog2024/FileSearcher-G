using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Scanning;

public class FileSystemScannerTests
{
    private static IndexProfile CreateProfile(string rootPath, string storagePath, params string[] extensions) => new()
    {
        Name = "测试索引",
        RootPath = rootPath,
        StoragePath = storagePath,
        IncludedExtensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase),
        IncrementalEnabled = true,
    };

    private static async Task<List<ScanItemResult>> RunScanAsync(FileSystemScanner scanner, IndexProfile profile, ManifestStore manifest)
    {
        var results = new List<ScanItemResult>();
        await foreach (var item in scanner.ScanAsync(profile, manifest, pauseController: null, progress: null, CancellationToken.None))
        {
            results.Add(item);
        }
        return results;
    }

    [Fact]
    public async Task ScanAsync_FreshDirectory_MarksAllMatchingFilesAsAdded()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("a.txt", "内容A");
        source.CreateTextFile("sub/b.txt", "内容B");
        source.CreateTextFile("ignored.exe", "不应被扫描"); // 未勾选类型

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        using var manifest = ManifestStore.Open(profile.ManifestDbPath);
        var scanner = new FileSystemScanner();

        var results = await RunScanAsync(scanner, profile, manifest);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(ScanItemStatus.Added, r.Status));
        Assert.All(results, r => Assert.True(r.Document!.ContentExtracted));
    }

    [Fact]
    public async Task ScanAsync_UnselectedExtension_IsExcludedFromResults()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("a.txt", "内容A");
        source.CreateTextFile("b.json", "{}");

        var profile = CreateProfile(source.Path, storage.Path, "txt"); // 只勾选txt
        using var manifest = ManifestStore.Open(profile.ManifestDbPath);
        var scanner = new FileSystemScanner();

        var results = await RunScanAsync(scanner, profile, manifest);

        Assert.Single(results);
        Assert.EndsWith("a.txt", results[0].FullPath);
    }

    [Fact]
    public async Task ScanAsync_SecondRunWithoutChanges_MarksFilesAsUnchanged()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("a.txt", "内容A");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        var scanner = new FileSystemScanner();

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            var firstRun = await RunScanAsync(scanner, profile, manifest);
            Assert.All(firstRun, r => Assert.Equal(ScanItemStatus.Added, r.Status));
        }

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            var secondRun = await RunScanAsync(scanner, profile, manifest);
            Assert.Single(secondRun);
            Assert.Equal(ScanItemStatus.Unchanged, secondRun[0].Status);
            Assert.Null(secondRun[0].Document); // Unchanged 不重新产出文档内容
        }
    }

    [Fact]
    public async Task ScanAsync_ModifiedFile_MarksAsUpdatedAndReextractsContent()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        string filePath = source.CreateTextFile("a.txt", "旧内容");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        var scanner = new FileSystemScanner();

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            await RunScanAsync(scanner, profile, manifest);
        }

        // 确保修改时间戳会发生变化（部分文件系统mtime精度较低，需要保证至少跨过1个可分辨单位）
        await Task.Delay(50);
        File.WriteAllText(filePath, "新内容已更新");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(5));

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            var secondRun = await RunScanAsync(scanner, profile, manifest);
            Assert.Single(secondRun);
            Assert.Equal(ScanItemStatus.Updated, secondRun[0].Status);
            Assert.Contains("新内容已更新", secondRun[0].Document!.Content);
        }
    }

    [Fact]
    public async Task ScanAsync_DeletedFile_IsReportedAsRemovedOnNextScan()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        string filePath = source.CreateTextFile("a.txt", "内容A");
        source.CreateTextFile("b.txt", "内容B");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        var scanner = new FileSystemScanner();

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            await RunScanAsync(scanner, profile, manifest);
        }

        File.Delete(filePath);

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            var secondRun = await RunScanAsync(scanner, profile, manifest);

            var removed = secondRun.Where(r => r.Status == ScanItemStatus.Removed).ToList();
            Assert.Single(removed);
            Assert.EndsWith("a.txt", removed[0].FullPath);

            var unchanged = secondRun.Where(r => r.Status == ScanItemStatus.Unchanged).ToList();
            Assert.Single(unchanged);
            Assert.EndsWith("b.txt", unchanged[0].FullPath);
        }
    }

    [Fact]
    public async Task ScanAsync_PptFile_MarksFileNameOnlyPerPlanC()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("slides.ppt", "这不是一个真实的ppt二进制格式，但方案C下根本不会尝试解析内容");

        var profile = CreateProfile(source.Path, storage.Path, "ppt");
        using var manifest = ManifestStore.Open(profile.ManifestDbPath);
        var scanner = new FileSystemScanner();

        var results = await RunScanAsync(scanner, profile, manifest);

        Assert.Single(results);
        Assert.Equal(ScanItemStatus.Added, results[0].Status);
        Assert.False(results[0].Document!.ContentExtracted);
        Assert.Equal(string.Empty, results[0].Document!.Content);
        Assert.Contains("仅索引文件名", results[0].Document!.SkipOrFailureReason);
    }

    [Fact]
    public async Task ScanAsync_OversizedFile_SkipsContentExtractionButStillIndexesFileName()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("huge.txt", "内容");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        profile.MaxFileSizeForContentBytes = 1; // 人为设置极小上限，强制触发"超大文件"分支

        using var manifest = ManifestStore.Open(profile.ManifestDbPath);
        var scanner = new FileSystemScanner();

        var results = await RunScanAsync(scanner, profile, manifest);

        Assert.Single(results);
        Assert.False(results[0].Document!.ContentExtracted);
        Assert.Contains("MB 上限", results[0].Document!.SkipOrFailureReason);
    }

    [Fact]
    public async Task ScanAsync_NonIncrementalMode_DoesNotReportRemovedFiles()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        string filePath = source.CreateTextFile("a.txt", "内容A");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        var scanner = new FileSystemScanner();

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            await RunScanAsync(scanner, profile, manifest);
        }

        File.Delete(filePath);
        profile.IncrementalEnabled = false; // 全量模式下不做"识别已删除文件"这一步

        using (var manifest = ManifestStore.Open(profile.ManifestDbPath))
        {
            var secondRun = await RunScanAsync(scanner, profile, manifest);
            Assert.DoesNotContain(secondRun, r => r.Status == ScanItemStatus.Removed);
        }
    }

    [Fact]
    public async Task ScanAsync_InaccessibleSubdirectory_DoesNotAbortEntireScan()
    {
        // UnixFileMode 仅在类 Unix 平台受支持（用于在本沙盒的 Linux 环境下模拟"权限不足的子目录"），
        // Windows 上的等效场景（NTFS ACL 拒绝访问）已经由扫描器里对 UnauthorizedAccessException 的
        // 捕获逻辑覆盖，属于同一段生产代码路径，因此在 Windows 上直接跳过本测试不会削弱覆盖率的实际意义。
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("accessible.txt", "可访问文件");
        string restrictedDir = System.IO.Path.Combine(source.Path, "restricted");
        Directory.CreateDirectory(restrictedDir);
        File.WriteAllText(System.IO.Path.Combine(restrictedDir, "secret.txt"), "受限内容");

        try
        {
            // 移除所有权限，模拟"权限不足的子目录"场景
#pragma warning disable CA1416 // 已在方法开头做过平台检查，此处保证只在类 Unix 平台执行
            var dirInfo = new DirectoryInfo(restrictedDir);
            dirInfo.UnixFileMode = System.IO.UnixFileMode.None;
#pragma warning restore CA1416

            var profile = CreateProfile(source.Path, storage.Path, "txt");
            using var manifest = ManifestStore.Open(profile.ManifestDbPath);
            var scanner = new FileSystemScanner();

            var results = await RunScanAsync(scanner, profile, manifest);

            // 受限目录被跳过，但兄弟文件仍应正常被扫描到，扫描过程本身不应抛异常/中断
            Assert.Contains(results, r => r.FullPath.EndsWith("accessible.txt"));
        }
        finally
        {
            // 测试清理前必须恢复权限，否则 TempDirectory.Dispose() 递归删除会失败
#pragma warning disable CA1416
            try { new DirectoryInfo(restrictedDir).UnixFileMode = System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute; } catch { }
#pragma warning restore CA1416
        }
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressForEachProcessedFile()
    {
        using var source = new TempDirectory();
        using var storage = new TempDirectory();
        source.CreateTextFile("a.txt", "A");
        source.CreateTextFile("b.txt", "B");

        var profile = CreateProfile(source.Path, storage.Path, "txt");
        using var manifest = ManifestStore.Open(profile.ManifestDbPath);
        var scanner = new FileSystemScanner();

        var progressReports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => progressReports.Add(new ScanProgress
        {
            FilesScanned = p.FilesScanned,
            FilesIndexed = p.FilesIndexed,
        }));

        await foreach (var _ in scanner.ScanAsync(profile, manifest, null, progress, CancellationToken.None))
        {
            // 消费流即可，进度通过 IProgress 回调收集
        }

        // Progress<T> 的回调是通过 SynchronizationContext.Post 异步排队的，
        // 测试环境没有消息循环，这里等待一小段时间让回调队列排空，避免测试出现偶发性失败。
        await Task.Delay(200);

        Assert.Equal(2, progressReports.Count);
        Assert.Equal(2, progressReports[^1].FilesScanned);
        Assert.Equal(2, progressReports[^1].FilesIndexed);
    }
}
