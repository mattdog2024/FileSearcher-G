using FileTrace.Core.Models;
using FileTrace.Core.Persistence;
using FileTrace.Core.Tests.TestHelpers;
using Xunit;

namespace FileTrace.Core.Tests.Persistence;

public class IndexProfileStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string RegistryPath => Path.Combine(_temp.Path, "registry.json");

    [Fact]
    public async Task LoadAllAsync_NoRegistryFile_ReturnsEmptyList()
    {
        var store = new IndexProfileStore(RegistryPath);

        var profiles = await store.LoadAllAsync();

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAll_RoundTripsProfile()
    {
        var store = new IndexProfileStore(RegistryPath);
        string storagePath = Path.Combine(_temp.Path, "work-index");

        var profile = new IndexProfile
        {
            Name = "工作文档",
            RootPath = @"D:\Documents\Work",
            StoragePath = storagePath,
            IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "docx", "pdf" },
            IncrementalEnabled = true,
            FileCount = 42,
            TotalSizeBytes = 12345,
        };

        await store.SaveAsync(profile);

        var loaded = await store.LoadAllAsync();

        var reloaded = Assert.Single(loaded);
        Assert.Equal("工作文档", reloaded.Name);
        Assert.Equal(@"D:\Documents\Work", reloaded.RootPath);
        Assert.Equal(storagePath, reloaded.StoragePath);
        Assert.Contains("docx", reloaded.IncludedExtensions);
        Assert.Contains("pdf", reloaded.IncludedExtensions);
        Assert.Equal(42, reloaded.FileCount);
        Assert.Equal(12345, reloaded.TotalSizeBytes);
        Assert.True(File.Exists(Path.Combine(storagePath, "profile.json")));
    }

    [Fact]
    public async Task SaveAsync_CalledTwiceForSamePath_DoesNotDuplicateRegistryEntry()
    {
        var store = new IndexProfileStore(RegistryPath);
        string storagePath = Path.Combine(_temp.Path, "dup-index");
        var profile = new IndexProfile { Name = "A", RootPath = "C:\\A", StoragePath = storagePath };

        await store.SaveAsync(profile);
        profile.Name = "A-Renamed";
        await store.SaveAsync(profile);

        var loaded = await store.LoadAllAsync();

        var only = Assert.Single(loaded);
        Assert.Equal("A-Renamed", only.Name);
    }

    [Fact]
    public async Task SaveAsync_MultipleProfiles_AllLoadedBack()
    {
        var store = new IndexProfileStore(RegistryPath);

        await store.SaveAsync(new IndexProfile { Name = "索引1", RootPath = "C:\\1", StoragePath = Path.Combine(_temp.Path, "p1") });
        await store.SaveAsync(new IndexProfile { Name = "索引2", RootPath = "C:\\2", StoragePath = Path.Combine(_temp.Path, "p2") });
        await store.SaveAsync(new IndexProfile { Name = "索引3", RootPath = "C:\\3", StoragePath = Path.Combine(_temp.Path, "p3") });

        var loaded = await store.LoadAllAsync();

        Assert.Equal(3, loaded.Count);
        Assert.Contains(loaded, p => p.Name == "索引1");
        Assert.Contains(loaded, p => p.Name == "索引2");
        Assert.Contains(loaded, p => p.Name == "索引3");
    }

    [Fact]
    public async Task UnregisterAsync_RemovesFromRegistry_ButKeepsDiskFiles()
    {
        var store = new IndexProfileStore(RegistryPath);
        string storagePath = Path.Combine(_temp.Path, "removable");
        await store.SaveAsync(new IndexProfile { Name = "待删除", RootPath = "C:\\X", StoragePath = storagePath });

        await store.UnregisterAsync(storagePath);

        var loaded = await store.LoadAllAsync();
        Assert.Empty(loaded);
        Assert.True(File.Exists(Path.Combine(storagePath, "profile.json")), "取消注册不应删除磁盘上的 profile.json");
    }

    [Fact]
    public async Task LoadAllAsync_RegistryReferencesMissingProfileJson_SkipsGracefullyAndReportsSkipped()
    {
        // 模拟"注册表里记录了一个路径，但对应目录/profile.json 已经被用户手动删除"的场景。
        await File.WriteAllTextAsync(RegistryPath, "[\"" + Path.Combine(_temp.Path, "ghost").Replace("\\", "\\\\") + "\"]");
        var store = new IndexProfileStore(RegistryPath);

        var skipped = new List<string>();
        var loaded = await store.LoadAllAsync(skipped);

        Assert.Empty(loaded);
        Assert.Single(skipped);
    }

    [Fact]
    public async Task LoadAllAsync_CorruptedRegistryJson_ReturnsEmptyWithoutThrowing()
    {
        await File.WriteAllTextAsync(RegistryPath, "{ this is not valid json ][");
        var store = new IndexProfileStore(RegistryPath);

        var loaded = await store.LoadAllAsync();

        Assert.Empty(loaded);
    }
}
