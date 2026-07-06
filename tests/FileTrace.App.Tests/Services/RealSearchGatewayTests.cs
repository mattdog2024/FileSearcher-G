using FileTrace.App.Services;
using FileTrace.App.Services.Real;
using FileTrace.App.Tests.TestHelpers;
using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Search;
using Xunit;

namespace FileTrace.App.Tests.Services;

public class RealSearchGatewayTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<IndexProfile> BuildIndexedProfileAsync(string name, string content)
    {
        string rootPath = Path.Combine(_temp.Path, name, "root");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, name + ".txt"), content);

        var profile = new IndexProfile
        {
            Name = name,
            RootPath = rootPath,
            StoragePath = Path.Combine(_temp.Path, name, "storage"),
            IncludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "txt" },
        };

        var indexingService = new RealIndexingService();
        await indexingService.RunAsync(profile, rebuildFromScratch: true, new ScanPauseController(), null);

        return profile;
    }

    [Fact]
    public async Task SearchAsync_FindsDocumentAcrossMultipleIndexedProfiles()
    {
        var profileA = await BuildIndexedProfileAsync("profileA", "寻迹关键词alpha内容");
        var profileB = await BuildIndexedProfileAsync("profileB", "另一份不相关的内容");

        var gateway = new RealSearchGateway(() => new List<IndexProfile> { profileA, profileB });

        var result = await gateway.SearchAsync(
            new SearchRequest("寻迹", SearchFieldScope.FileNameAndContent, Array.Empty<string>()));

        Assert.Equal(1, result.TotalHits);
        Assert.Equal(profileA.Id, result.Items[0].ProfileId);
    }

    [Fact]
    public async Task SearchAsync_ProfileIdsFilterRestrictsSearchScope()
    {
        var profileA = await BuildIndexedProfileAsync("profileC", "共同关键词common出现在两处");
        var profileB = await BuildIndexedProfileAsync("profileD", "共同关键词common出现在两处");

        var gateway = new RealSearchGateway(() => new List<IndexProfile> { profileA, profileB });

        var result = await gateway.SearchAsync(
            new SearchRequest("common", SearchFieldScope.FileNameAndContent, new[] { profileA.Id }));

        Assert.Equal(1, result.TotalHits);
        Assert.Equal(profileA.Id, result.Items[0].ProfileId);
    }

    [Fact]
    public async Task SearchAsync_ProfileWithoutBuiltIndex_IsSilentlySkippedNotThrown()
    {
        // 模拟"索引配置存在，但从未成功构建过 Lucene 目录"的场景（例如新建后立即崩溃退出）。
        var neverBuilt = new IndexProfile
        {
            Name = "从未构建",
            RootPath = Path.Combine(_temp.Path, "neverbuilt-root"),
            StoragePath = Path.Combine(_temp.Path, "neverbuilt-storage"),
        };

        var gateway = new RealSearchGateway(() => new List<IndexProfile> { neverBuilt });

        var result = await gateway.SearchAsync(
            new SearchRequest("任意关键词", SearchFieldScope.FileNameAndContent, Array.Empty<string>()));

        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchAsync_SourceDiskOfflineButIndexStillOnDisk_StillReturnsResults()
    {
        // 核心卖点验证：即使 RootPath 指向的目录已经"消失"（模拟硬盘拔出），
        // 只要 Lucene 索引数据（StoragePath）还在，依然可以正常搜索到文件名/内容。
        var profile = await BuildIndexedProfileAsync("offline-profile", "离线也能搜索到的内容mustfind");

        // 模拟源盘拔出：删除 RootPath，但保留 StoragePath 下的索引数据。
        Directory.Delete(profile.RootPath, recursive: true);

        var gateway = new RealSearchGateway(() => new List<IndexProfile> { profile });
        var result = await gateway.SearchAsync(
            new SearchRequest("mustfind", SearchFieldScope.FileNameAndContent, Array.Empty<string>()));

        Assert.Equal(1, result.TotalHits);
    }
}
