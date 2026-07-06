using FileTrace.Core.Search;

namespace FileTrace.App.Services;

/// <summary>
/// 搜索请求参数：查询语句 + 字段范围 + 参与检索的索引 Id 列表。
/// </summary>
public sealed record SearchRequest(
    string Query,
    SearchFieldScope Scope,
    IReadOnlyList<string> ProfileIds);

/// <summary>
/// 搜索能力的抽象。Stage2 由 <see cref="Mock.MockSearchGateway"/> 返回内置示例数据用于联调 UI 交互
/// （加载态、空结果态、高亮渲染），Stage3 替换为基于 <see cref="SearchService"/> 的真实实现，
/// 届时会根据勾选的索引 Id 动态选取对应的 lucene 目录构造 MultiReader。
/// </summary>
public interface ISearchGateway
{
    Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
