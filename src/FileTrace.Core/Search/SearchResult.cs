namespace FileTrace.Core.Search;

/// <summary>一条搜索结果，对应设计稿 ResultCard 展示所需的全部字段。</summary>
public sealed class SearchResultItem
{
    /// <summary>该结果所属的 IndexProfile.Id，用于展示"来自哪个索引"以及判断源盘是否在线。</summary>
    public required string ProfileId { get; init; }

    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required string DirectoryPath { get; init; }
    public required string ExtensionNoDot { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset LastWriteTimeUtc { get; init; }
    public required bool ContentIndexed { get; init; }

    /// <summary>相关性得分（Lucene 原始 score），用于排序展示，数值本身对用户没有直接意义。</summary>
    public required float Score { get; init; }

    /// <summary>
    /// 文件名字段的高亮片段（命中关键词用 &lt;mark&gt;...&lt;/mark&gt; 包裹），
    /// 若该字段未命中任何查询词则为 null。
    /// </summary>
    public string? FileNameHighlight { get; init; }

    /// <summary>
    /// 内容字段的高亮摘要片段（截取命中关键词附近的上下文，用 &lt;mark&gt;...&lt;/mark&gt; 包裹命中词），
    /// 若内容未建立索引或未命中任何查询词则为 null。
    /// </summary>
    public string? ContentHighlight { get; init; }
}

/// <summary>一次搜索的完整结果集。</summary>
public sealed class SearchResult
{
    public required IReadOnlyList<SearchResultItem> Items { get; init; }

    /// <summary>命中的文档总数（可能大于 Items.Count，取决于分页/TopN 限制）。</summary>
    public required int TotalHits { get; init; }
}
