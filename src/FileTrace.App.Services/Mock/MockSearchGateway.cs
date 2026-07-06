using FileTrace.Core.Search;

namespace FileTrace.App.Services.Mock;

/// <summary>
/// Stage2 UI 骨架阶段使用的假搜索网关：不接触真实 Lucene 索引，
/// 根据关键词是否为空返回一组内置示例结果（含高亮标记字符串），
/// 用于联调加载动画、空结果态、结果卡片高亮渲染等纯 UI 交互。
/// Stage3 会替换为基于 <see cref="SearchService"/> 对接真实索引目录的实现。
/// </summary>
public sealed class MockSearchGateway : ISearchGateway
{
    public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        // 模拟真实检索的网络/IO 延迟，让 UI 的"搜索中"加载态有意义可见。
        await Task.Delay(280, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new SearchResult { Items = Array.Empty<SearchResultItem>(), TotalHits = 0 };
        }

        var keyword = request.Query.Trim();

        var allItems = new List<SearchResultItem>
        {
            new()
            {
                ProfileId = "demo-work",
                FullPath = @"D:\Documents\Work\2024年度总结报告.docx",
                FileName = "2024年度总结报告.docx",
                DirectoryPath = @"D:\Documents\Work",
                ExtensionNoDot = "docx",
                SizeBytes = 1_258_000,
                LastWriteTimeUtc = DateTimeOffset.Now.AddDays(-6),
                ContentIndexed = true,
                Score = 9.82f,
                FileNameHighlight = $"2024年度<mark>总结</mark>报告.docx",
                ContentHighlight = $"...本季度围绕核心业务开展<mark>{keyword}</mark>相关工作，累计完成需求交付 47 项，" +
                                    $"较上季度提升 23%。在<mark>{keyword}</mark>方向持续投入研发资源...",
            },
            new()
            {
                ProfileId = "demo-work",
                FullPath = @"D:\Documents\Work\预算表-Q3.xlsx",
                FileName = "预算表-Q3.xlsx",
                DirectoryPath = @"D:\Documents\Work",
                ExtensionNoDot = "xlsx",
                SizeBytes = 86_200,
                LastWriteTimeUtc = DateTimeOffset.Now.AddDays(-14),
                ContentIndexed = true,
                Score = 7.14f,
                FileNameHighlight = "预算表-Q3.xlsx",
                ContentHighlight = $"部门 | 预算 | 备注\n研发部 | 120万 | 含<mark>{keyword}</mark>专项预算\n市场部 | 80万 | -",
            },
            new()
            {
                ProfileId = "demo-projects",
                FullPath = @"E:\Projects\FileTrace\src\Search\SearchService.cs",
                FileName = "SearchService.cs",
                DirectoryPath = @"E:\Projects\FileTrace\src\Search",
                ExtensionNoDot = "cs",
                SizeBytes = 15_360,
                LastWriteTimeUtc = DateTimeOffset.Now.AddHours(-5),
                ContentIndexed = true,
                Score = 6.03f,
                FileNameHighlight = "SearchService.cs",
                ContentHighlight = $"public SearchResult Search(string rawQuery, ...) {{ // 处理<mark>{keyword}</mark>查询语法解析 }}",
            },
            new()
            {
                ProfileId = "demo-backup",
                FullPath = @"F:\Backup2023\发布会\新品发布会流程.pptx",
                FileName = "新品发布会流程.pptx",
                DirectoryPath = @"F:\Backup2023\发布会",
                ExtensionNoDot = "pptx",
                SizeBytes = 4_820_000,
                LastWriteTimeUtc = DateTimeOffset.Now.AddMonths(-5),
                ContentIndexed = false,
                Score = 3.21f,
                FileNameHighlight = $"新品<mark>发布</mark>会流程.pptx",
                ContentHighlight = null,
            },
            new()
            {
                ProfileId = "demo-work",
                FullPath = @"D:\Documents\Work\合同扫描件.pdf",
                FileName = "合同扫描件.pdf",
                DirectoryPath = @"D:\Documents\Work",
                ExtensionNoDot = "pdf",
                SizeBytes = 2_130_000,
                LastWriteTimeUtc = DateTimeOffset.Now.AddDays(-40),
                ContentIndexed = true,
                Score = 2.87f,
                FileNameHighlight = "合同扫描件.pdf",
                ContentHighlight = $"...甲方与乙方就<mark>{keyword}</mark>相关条款达成一致，自签订之日起生效...",
            },
        };

        // 简单模拟"关键词不同、命中数量不同"的体验：短查询命中全部示例，长查询/生僻词只命中少数。
        var matched = keyword.Length <= 6
            ? allItems
            : allItems.Take(2).ToList();

        return new SearchResult { Items = matched, TotalHits = matched.Count };
    }
}
