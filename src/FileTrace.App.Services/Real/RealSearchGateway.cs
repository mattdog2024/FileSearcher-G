using System.IO;
using FileTrace.Core.Models;
using FileTrace.Core.Search;

namespace FileTrace.App.Services.Real;

/// <summary>
/// <see cref="ISearchGateway"/> 的真实实现：根据 <see cref="SearchRequest.ProfileIds"/>
/// 动态定位对应的 Lucene 索引目录（通过注入的 profile 查找函数），构造
/// <see cref="SearchService"/>（单索引或 MultiReader 联合搜索）执行真实检索。
///
/// 关键设计：每次搜索都新建一个短生命周期的 SearchService 并在 using 结束时释放，
/// 而不是常驻持有一个 IndexReader——因为索引会随着"重建索引/增量更新"不断发生写入变化，
/// 常驻 Reader 需要额外的"Reader 是否过期(NRT)"管理逻辑，Stage3 阶段优先选择更简单可靠的
/// "每次查询重新打开"策略；后续如果大规模索引下频繁打开 Reader 出现明显性能问题，
/// 可以在这里替换为 SearcherManager/NRT 常驻方案而不影响上层 ISearchGateway 契约。
/// </summary>
public sealed class RealSearchGateway : ISearchGateway
{
    private readonly Func<IReadOnlyList<IndexProfile>> _profilesProvider;

    /// <param name="profilesProvider">
    /// 返回当前全部已知索引配置的委托（通常指向 MainViewModel 内存中的 IndexProfiles 快照），
    /// 用于把请求里的 ProfileId 列表解析为对应的 Lucene 索引目录路径。
    /// </param>
    public RealSearchGateway(Func<IReadOnlyList<IndexProfile>> profilesProvider)
    {
        _profilesProvider = profilesProvider;
    }

    public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var allProfiles = _profilesProvider();
        var targetProfiles = request.ProfileIds.Count == 0
            ? allProfiles
            : allProfiles.Where(p => request.ProfileIds.Contains(p.Id)).ToList();

        // 只对"索引数据实际存在于磁盘上"的 profile 发起搜索——例如源盘已拔出但索引数据还在，
        // 这种索引仍然应该参与搜索（这正是产品的核心卖点）；但如果 Lucene 目录本身缺失/为空
        // （从未构建过），打开 SearchService 会抛异常，需要提前过滤掉。
        var luceneDirectories = targetProfiles
            .Where(p => Directory.Exists(p.LuceneDirectory) && Directory.EnumerateFileSystemEntries(p.LuceneDirectory).Any())
            .Select(p => p.LuceneDirectory)
            .ToList();

        if (string.IsNullOrWhiteSpace(request.Query) || luceneDirectories.Count == 0)
        {
            return Task.FromResult(new SearchResult { Items = Array.Empty<SearchResultItem>(), TotalHits = 0 });
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Lucene 的 IndexSearcher.Search 是同步阻塞调用（IO + 打分计算），
        // 用 Task.Run 切到线程池执行，避免在 UI 线程上直接阻塞导致界面卡顿。
        return Task.Run(() =>
        {
            using var searchService = new SearchService(luceneDirectories);
            return searchService.Search(request.Query, request.Scope);
        }, cancellationToken);
    }
}
