using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;

namespace FileTrace.Core.Search;

/// <summary>限定一次搜索作用于哪些字段。</summary>
public enum SearchFieldScope
{
    /// <summary>默认：文件名与内容任一命中即可（每个关键词在"文件名 或 内容"里出现即可满足该关键词）。</summary>
    FileNameAndContent,

    /// <summary>只搜文件名（对应 UI 上"仅搜索文件名"开关）。</summary>
    FileNameOnly,

    /// <summary>只搜内容。</summary>
    ContentOnly,
}

/// <summary>
/// 寻迹搜索语法的解析器与 Lucene <see cref="Query"/> 构造器。
///
/// 支持的语法（对应产品需求"精确短语/排除词/通配符+文件名搜索"）：
///   - 普通关键词：多个关键词之间默认 AND（全部关键词都要命中，每个关键词在文件名或内容中任一处出现即可）；
///   - "精确短语"：双引号包裹的内容按短语匹配（要求词序相邻，不允许拆开命中）；
///   - -排除词 / -"排除短语"：以短号开头的词（或短语）视为排除条件，命中该词的文档会被剔除；
///   - 通配符：* 匹配任意长度字符，? 匹配单个字符，例如 报告*.docx、201?年度总结；
///   - 字段限定前缀：filename:关键词 / name:关键词 只匹配文件名，content:关键词 只匹配正文内容
///     （不加前缀时遵循传入的 <see cref="SearchFieldScope"/>，默认文件名与内容任一命中即可）。
///
/// 设计取舍：没有直接使用 Lucene 自带的 <c>QueryParser</c>/<c>MultiFieldQueryParser</c>，
/// 因为它们的转义规则、通配符处理与"多字段 OR 但多关键词 AND"的语义组合较难精确控制，
/// 且中文分词场景下 QueryParser 对短语查询的分词边界处理不够直观。这里用一个轻量的自定义
/// 词法分析器把原始查询串切成"短语/普通词/通配符词 + 是否排除 + 字段限定"的子句列表，
/// 再逐个转换为 Lucene 底层 Query 对象手动拼装，行为完全可控、也更容易写单元测试覆盖。
/// </summary>
public static class SearchQueryBuilder
{
    /// <summary>
    /// 将用户输入的原始查询字符串解析为 Lucene <see cref="Query"/>。
    /// </summary>
    /// <param name="rawQuery">用户在搜索框输入的原始文本。</param>
    /// <param name="defaultScope">未使用字段前缀的关键词默认搜索范围。</param>
    /// <param name="analyzer">
    /// 用于把普通词/短语文本分析成索引期一致的词元；调用方通常传入
    /// <see cref="IndexWriterService.CreateAnalyzer"/> 或直接 new <see cref="FileTraceAnalyzer"/>()。
    /// </param>
    /// <returns>
    /// 构造好的查询；若解析结果不包含任何有效子句（例如空字符串、纯空白），返回 null，
    /// 调用方应将其视为"无效查询，不执行搜索"。
    /// </returns>
    public static Query? Build(string rawQuery, SearchFieldScope defaultScope, Analyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);

        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return null;
        }

        List<SearchClause> clauses = Tokenize(rawQuery);
        if (clauses.Count == 0)
        {
            return null;
        }

        var queryBuilder = new QueryBuilder(analyzer);
        var outerQuery = new BooleanQuery();
        bool hasPositiveClause = false;

        foreach (var clause in clauses)
        {
            Query? fieldQuery = BuildClauseQuery(clause, defaultScope, queryBuilder);
            if (fieldQuery is null)
            {
                // 分词后未产生任何词元（例如短语内容全是停用词/空白），跳过该子句。
                continue;
            }

            if (clause.IsExcluded)
            {
                outerQuery.Add(fieldQuery, Occur.MUST_NOT);
            }
            else
            {
                outerQuery.Add(fieldQuery, Occur.MUST);
                hasPositiveClause = true;
            }
        }

        if (outerQuery.Clauses.Count == 0)
        {
            return null;
        }

        // Lucene 的 BooleanQuery 如果只包含 MUST_NOT 子句（纯排除、没有任何"必须命中"的正向条件），
        // 语义上无法确定候选文档全集，需要补一个 MatchAllDocsQuery 作为基准全集，再从中排除。
        if (!hasPositiveClause)
        {
            var withBase = new BooleanQuery { { new MatchAllDocsQuery(), Occur.MUST } };
            foreach (BooleanClause existing in outerQuery.Clauses)
            {
                withBase.Add(existing);
            }
            return withBase;
        }

        return outerQuery;
    }

    private static Query? BuildClauseQuery(SearchClause clause, SearchFieldScope defaultScope, QueryBuilder queryBuilder)
    {
        SearchFieldScope scope = clause.FieldScope ?? defaultScope;

        return clause.Kind switch
        {
            SearchClauseKind.Wildcard => BuildWildcardQuery(clause.Text, scope),
            SearchClauseKind.Phrase => BuildMultiFieldQuery(
                scope, field => queryBuilder.CreatePhraseQuery(field, clause.Text)),
            _ => BuildMultiFieldQuery(
                scope, field => queryBuilder.CreateBooleanQuery(field, clause.Text, Occur.MUST)),
        };
    }

    /// <summary>
    /// 按作用域把"针对单个字段构造 Query"的委托应用到文件名/内容其中一个或两个字段上，
    /// 多字段时用 SHOULD（任一命中即可）组合成一个整体子句。
    /// </summary>
    private static Query? BuildMultiFieldQuery(SearchFieldScope scope, Func<string, Query?> buildForField)
    {
        var fields = scope switch
        {
            SearchFieldScope.FileNameOnly => new[] { IndexFieldNames.FileName },
            SearchFieldScope.ContentOnly => new[] { IndexFieldNames.Content },
            _ => new[] { IndexFieldNames.FileName, IndexFieldNames.Content },
        };

        var subQueries = fields
            .Select(buildForField)
            .Where(q => q is not null)
            .Cast<Query>()
            .ToList();

        if (subQueries.Count == 0)
        {
            return null;
        }

        if (subQueries.Count == 1)
        {
            return subQueries[0];
        }

        var boolean = new BooleanQuery();
        foreach (var q in subQueries)
        {
            boolean.Add(q, Occur.SHOULD);
        }
        return boolean;
    }

    /// <summary>
    /// 通配符查询：文件名走 <see cref="IndexFieldNames.FileNameKeyword"/>（整词不分词，小写化），
    /// 能精确支持 *.docx / 报告2024* 这类对完整文件名做匹配的场景；内容走
    /// <see cref="IndexFieldNames.Content"/> 字段的已分词词元做通配符匹配（因为中文分词后是分词单元，
    /// 内容通配符的匹配粒度是"词"而非任意字符位置，这是中文全文检索通配符的普遍限制，
    /// 与 Archivarius 3000 等同类工具的行为预期一致）。
    /// </summary>
    private static Query BuildWildcardQuery(string text, SearchFieldScope scope)
    {
        string normalized = text.ToLowerInvariant();

        return scope switch
        {
            SearchFieldScope.FileNameOnly =>
                new WildcardQuery(new Term(IndexFieldNames.FileNameKeyword, normalized)),
            SearchFieldScope.ContentOnly =>
                new WildcardQuery(new Term(IndexFieldNames.Content, normalized)),
            _ => new BooleanQuery
            {
                { new WildcardQuery(new Term(IndexFieldNames.FileNameKeyword, normalized)), Occur.SHOULD },
                { new WildcardQuery(new Term(IndexFieldNames.Content, normalized)), Occur.SHOULD },
            },
        };
    }

    /// <summary>
    /// 词法分析：把原始查询字符串切成一组 <see cref="SearchClause"/>。
    /// 识别规则：
    ///   - 空白分隔词与词之间的边界（引号内的空白不作为分隔符）；
    ///   - 前导 '-'（紧贴后面的词/短语，中间不能有空格）标记该子句为排除；
    ///   - 双引号包裹的内容整体作为一个短语子句（内部空白保留）；
    ///   - "filename:"/"name:"/"content:" 前缀（大小写不敏感）限定该子句的搜索字段；
    ///   - 词中包含 '*' 或 '?' 时识别为通配符子句。
    /// </summary>
    private static List<SearchClause> Tokenize(string rawQuery)
    {
        var clauses = new List<SearchClause>();
        int i = 0;
        int length = rawQuery.Length;

        while (i < length)
        {
            if (char.IsWhiteSpace(rawQuery[i]))
            {
                i++;
                continue;
            }

            bool isExcluded = false;
            if (rawQuery[i] == '-' && i + 1 < length && !char.IsWhiteSpace(rawQuery[i + 1]))
            {
                isExcluded = true;
                i++;
            }

            string rawToken;
            bool isPhrase;

            if (i < length && rawQuery[i] == '"')
            {
                i++; // 跳过开引号
                int start = i;
                while (i < length && rawQuery[i] != '"')
                {
                    i++;
                }
                rawToken = rawQuery.Substring(start, i - start);
                if (i < length)
                {
                    i++; // 跳过闭引号
                }
                isPhrase = true;
            }
            else
            {
                int start = i;
                while (i < length && !char.IsWhiteSpace(rawQuery[i]))
                {
                    i++;
                }
                rawToken = rawQuery.Substring(start, i - start);
                isPhrase = false;
            }

            if (string.IsNullOrWhiteSpace(rawToken))
            {
                continue;
            }

            SearchFieldScope? explicitScope = null;
            string text = rawToken;
            foreach (var (prefix, scope) in FieldPrefixes)
            {
                if (rawToken.Length > prefix.Length &&
                    rawToken.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    explicitScope = scope;
                    text = rawToken[prefix.Length..];
                    break;
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            SearchClauseKind kind = !isPhrase && ContainsWildcardChar(text)
                ? SearchClauseKind.Wildcard
                : isPhrase ? SearchClauseKind.Phrase : SearchClauseKind.Term;

            clauses.Add(new SearchClause(kind, text, isExcluded, explicitScope));
        }

        return clauses;
    }

    private static bool ContainsWildcardChar(string text) => text.Contains('*') || text.Contains('?');

    private static readonly (string Prefix, SearchFieldScope Scope)[] FieldPrefixes =
    {
        ("filename:", SearchFieldScope.FileNameOnly),
        ("name:", SearchFieldScope.FileNameOnly),
        ("content:", SearchFieldScope.ContentOnly),
    };

    private enum SearchClauseKind
    {
        Term,
        Phrase,
        Wildcard,
    }

    private sealed record SearchClause(
        SearchClauseKind Kind,
        string Text,
        bool IsExcluded,
        SearchFieldScope? FieldScope);
}
