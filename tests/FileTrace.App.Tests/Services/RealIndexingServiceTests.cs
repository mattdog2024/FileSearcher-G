using FileTrace.App.Services.Real;
using FileTrace.App.Tests.TestHelpers;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
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
