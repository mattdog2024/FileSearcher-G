using Lucene.Net.Analysis;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Store;

namespace FileTrace.Core.Search;

/// <summary>
/// 搜索入口：支持对单个索引目录搜索，也支持用 <see cref="MultiReader"/> 一次性跨多个
/// <see cref="Models.IndexProfile"/> 联合搜索（对应产品需求"多块硬盘的索引可以合并搜索"）。
///
/// 离线可用性说明：搜索过程只读取 Lucene 索引目录本身（manifest.db 与 lucene/ 段文件），
/// 完全不需要访问 IndexProfile.RootPath 指向的原始磁盘——这正是"硬盘拔出后依然能搜索到
/// 文件名+内容关键词、并定位到文件原路径"这一核心卖点的底层实现基础：只要索引数据还在
/// （哪怕保存在另一块盘上），搜索和高亮都能正常工作，只是点击"打开文件"时若原盘不在会失败，
/// 那是 UI 层需要处理的另一个问题，不属于本搜索服务的职责。
/// </summary>
public sealed class SearchService : IDisposable
{
    private const int DefaultFragmentCharSize = 80;

    private readonly List<FSDirectory> _directories = new();
    private readonly List<IndexReader> _subReaders = new();
    private readonly IndexReader _reader;
    private readonly IndexSearcher _searcher;
    private readonly Analyzer _analyzer;
    private bool _disposed;

    /// <summary>打开单个索引目录用于搜索。</summary>
    public SearchService(string luceneDirectoryPath)
        : this(new[] { luceneDirectoryPath })
    {
    }

    /// <summary>
    /// 同时打开多个索引目录，合并为一个联合只读视图用于跨索引搜索
    /// （例如用户希望"同时搜索移动硬盘A和移动硬盘B里已建立的索引"）。
    /// </summary>
    public SearchService(IReadOnlyCollection<string> luceneDirectoryPaths)
    {
        if (luceneDirectoryPaths is null || luceneDirectoryPaths.Count == 0)
        {
            throw new ArgumentException("至少需要提供一个索引目录", nameof(luceneDirectoryPaths));
        }

        _analyzer = IndexWriterService.CreateAnalyzer();

        foreach (var path in luceneDirectoryPaths)
        {
            var directory = FSDirectory.Open(path);
            _directories.Add(directory);
            _subReaders.Add(DirectoryReader.Open(directory));
        }

        _reader = _subReaders.Count == 1
            ? _subReaders[0]
            : new MultiReader(_subReaders.ToArray(), closeSubReaders: false);

        _searcher = new IndexSearcher(_reader);
    }

    /// <summary>
    /// 执行搜索。
    /// </summary>
    /// <param name="rawQuery">用户输入的原始查询串，语法见 <see cref="SearchQueryBuilder"/>。</param>
    /// <param name="scope">未显式指定字段前缀时的默认搜索范围。</param>
    /// <param name="maxResults">返回结果条数上限（TopN）。</param>
    /// <param name="highlight">是否生成高亮片段（关闭可略微提升纯统计/预取场景的性能）。</param>
    public SearchResult Search(
        string rawQuery,
        SearchFieldScope scope = SearchFieldScope.FileNameAndContent,
        int maxResults = 200,
        bool highlight = true)
    {
        ThrowIfDisposed();

        Query? query = SearchQueryBuilder.Build(rawQuery, scope, _analyzer);
        if (query is null)
        {
            return new SearchResult { Items = Array.Empty<SearchResultItem>(), TotalHits = 0 };
        }

        TopDocs topDocs = _searcher.Search(query, maxResults);

        Highlighter? contentHighlighter = null;
        Highlighter? fileNameHighlighter = null;
        if (highlight)
        {
            var scorer = new QueryScorer(query);
            var formatter = new SimpleHTMLFormatter("<mark>", "</mark>");
            contentHighlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new SimpleFragmenter(DefaultFragmentCharSize),
            };
            fileNameHighlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new NullFragmenter(),
            };
        }

        var items = new List<SearchResultItem>(topDocs.ScoreDocs.Length);
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            Document document = _searcher.Doc(scoreDoc.Doc);
            items.Add(ToResultItem(document, scoreDoc.Score, contentHighlighter, fileNameHighlighter));
        }

        return new SearchResult { Items = items, TotalHits = topDocs.TotalHits };
    }

    private SearchResultItem ToResultItem(
        Document document,
        float score,
        Highlighter? contentHighlighter,
        Highlighter? fileNameHighlighter)
    {
        string fullPath = document.Get(IndexFieldNames.Path) ?? string.Empty;
        string fileName = document.Get(IndexFieldNames.FileName) ?? string.Empty;
        string content = document.Get(IndexFieldNames.Content) ?? string.Empty;

        return new SearchResultItem
        {
            ProfileId = document.Get(IndexFieldNames.ProfileId) ?? string.Empty,
            FullPath = fullPath,
            FileName = fileName,
            DirectoryPath = document.Get(IndexFieldNames.DirectoryPath) ?? string.Empty,
            ExtensionNoDot = document.Get(IndexFieldNames.Extension) ?? string.Empty,
            SizeBytes = ParseInt64OrZero(document.Get(IndexFieldNames.SizeBytes)),
            LastWriteTimeUtc = new DateTimeOffset(
                ParseInt64OrZero(document.Get(IndexFieldNames.LastWriteTimeUtcTicks)), TimeSpan.Zero),
            ContentIndexed = document.Get(IndexFieldNames.ContentIndexed) == "1",
            Score = score,
            FileNameHighlight = TryHighlight(fileNameHighlighter, IndexFieldNames.FileName, fileName),
            ContentHighlight = TryHighlight(contentHighlighter, IndexFieldNames.Content, content),
        };
    }

    /// <summary>
    /// 对指定字段的原文尝试生成高亮片段；若该字段在这份文档里根本没有命中查询中的任何词
    /// （Highlighter 返回 null/空字符串），返回 null，调用方（UI）应回退展示原文的截断预览。
    /// </summary>
    private string? TryHighlight(Highlighter? highlighter, string fieldName, string fieldValue)
    {
        if (highlighter is null || string.IsNullOrEmpty(fieldValue))
        {
            return null;
        }

        try
        {
            string? fragment = highlighter.GetBestFragment(_analyzer, fieldName, fieldValue);
            return string.IsNullOrEmpty(fragment) ? null : fragment;
        }
        catch (IOException)
        {
            // Highlighter 内部会重新跑一次 Analyzer 分词流，理论上不会有真实 IO，
            // 这里兜底避免极端情况下的高亮失败影响整条搜索结果的返回。
            return null;
        }
    }

    private static long ParseInt64OrZero(string? value) =>
        long.TryParse(value, out long result) ? result : 0L;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SearchService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 单索引场景下 _reader 与 _subReaders[0] 是同一个实例，只需释放一次；
        // 多索引场景下 _reader 是包装了各 _subReaders 的 MultiReader（构造时传入
        // closeSubReaders: false，即 MultiReader.Dispose() 不会级联释放子 Reader），
        // 因此这里需要显式地把 MultiReader 与每个子 DirectoryReader 都释放一遍。
        if (_subReaders.Count > 1)
        {
            _reader.Dispose();
        }

        foreach (var subReader in _subReaders)
        {
            subReader.Dispose();
        }

        foreach (var directory in _directories)
        {
            directory.Dispose();
        }

        _disposed = true;
    }
}
