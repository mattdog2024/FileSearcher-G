using FileTrace.App.Services.Real;
using FileTrace.App.Tests.TestHelpers;
using FileTrace.Core.Models;
using Xunit;

namespace FileTrace.App.Tests.Services;

public class RealIndexProfileRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string RegistryPath => Path.Combine(_temp.Path, "data", "registry.json");

    [Fact]
    public async Task CreateAsync_ThenGetAll_RoundTripsProfileWithRefreshedNeedsUpdateStatus()
    {
        var repo = new RealIndexProfileRepository(RegistryPath);
        string rootPath = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(rootPath);
        string storagePath = Path.Combine(_temp.Path, "storage1");

        var profile = new IndexProfile
        {
            Name = "测试索引",
            RootPath = rootPath,
            StoragePath = storagePath,
            IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "txt" },
        };

        await repo.CreateAsync(profile);

        var all = await repo.GetAllAsync();
        var loaded = Assert.Single(all);

        Assert.Equal("测试索引", loaded.Name);
        // 从未跑过索引任务：Lucene 目录不存在 -> 运行时状态应刷新为 NeedsUpdate（源盘在线但索引未构建）。
        Assert.Equal(IndexStatus.NeedsUpdate, loaded.Status);
        Assert.Equal(0, loaded.FileCount);
    }

    [Fact]
    public async Task GetAllAsync_RootPathMissing_RefreshesStatusToSourceUnavailable()
    {
        var repo = new RealIndexProfileRepository(RegistryPath);
        string missingRoot = Path.Combine(_temp.Path, "does-not-exist");
        string storagePath = Path.Combine(_temp.Path, "storage2");

        await repo.CreateAsync(new IndexProfile
        {
            Name = "断开的索引",
            RootPath = missingRoot,
            StoragePath = storagePath,
        });

        var all = await repo.GetAllAsync();
        var loaded = Assert.Single(all);

        Assert.Equal(IndexStatus.SourceUnavailable, loaded.Status);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStorageDirectory_ButNeverTouchesRootPath()
    {
        var repo = new RealIndexProfileRepository(RegistryPath);
        string rootPath = Path.Combine(_temp.Path, "keep-me-root");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, "user-file.txt"), "不应被删除");

        string storagePath = Path.Combine(_temp.Path, "storage3");
        Directory.CreateDirectory(storagePath);

        var created = await repo.CreateAsync(new IndexProfile
        {
            Name = "待删除索引",
            RootPath = rootPath,
            StoragePath = storagePath,
        });

        await repo.DeleteAsync(created.Id);

        var all = await repo.GetAllAsync();
        Assert.Empty(all);
        Assert.False(Directory.Exists(storagePath), "索引存储目录应被彻底删除");
        Assert.True(Directory.Exists(rootPath), "绝不能删除用户的原始文件根目录");
        Assert.True(File.Exists(Path.Combine(rootPath, "user-file.txt")), "根目录下的用户文件必须保留");
    }

    [Fact]
    public async Task SaveAsync_PersistsUpdatedStatisticsAcrossReload()
    {
        var repo = new RealIndexProfileRepository(RegistryPath);
        string rootPath = Path.Combine(_temp.Path, "source4");
        Directory.CreateDirectory(rootPath);
        string storagePath = Path.Combine(_temp.Path, "storage4");

        var profile = await repo.CreateAsync(new IndexProfile
        {
            Name = "统计更新索引",
            RootPath = rootPath,
            StoragePath = storagePath,
        });

        profile.Name = "改名后的索引";
        await repo.SaveAsync(profile);

        var repo2 = new RealIndexProfileRepository(RegistryPath);
        var reloaded = Assert.Single(await repo2.GetAllAsync());
        Assert.Equal("改名后的索引", reloaded.Name);
    }

    [Fact]
    public async Task GetAllAsync_OrphanRegistryEntry_IsSelfHealedOnNextLoad()
    {
        var repo = new RealIndexProfileRepository(RegistryPath);
        string rootPath = Path.Combine(_temp.Path, "source5");
        Directory.CreateDirectory(rootPath);
        string storagePath = Path.Combine(_temp.Path, "storage5");

        await repo.CreateAsync(new IndexProfile
        {
            Name = "即将变孤儿的索引",
            RootPath = rootPath,
            StoragePath = storagePath,
        });

        // 模拟用户手动删除了整个索引存储目录（但没有走应用内的"删除索引"操作，
        // 所以注册表里还残留着这个已经不存在的 StoragePath）。
        Directory.Delete(storagePath, recursive: true);

        var all = await repo.GetAllAsync();
        Assert.Empty(all);

        // 第一次 GetAllAsync 应该已经把这个孤儿条目从注册表里清理掉了；
        // 用一个全新的仓库实例重新加载，验证注册表确实不会再尝试加载这个失效路径
        // （否则每次启动都会重复触发一次无意义的"加载失败"路径）。
        string registryContent = await File.ReadAllTextAsync(RegistryPath);
        Assert.DoesNotContain(storagePath, registryContent);

        var repo2 = new RealIndexProfileRepository(RegistryPath);
        var reloadedAll = await repo2.GetAllAsync();
        Assert.Empty(reloadedAll);
    }
}
