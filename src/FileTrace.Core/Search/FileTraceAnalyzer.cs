using JiebaNet.Segmenter;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;

namespace FileTrace.Core.Search;

/// <summary>
/// 寻迹 FileTrace 的自定义 Lucene 分词器：中文用 jieba.NET 分词，英文/数字统一小写化，
/// 用于 <see cref="IndexFieldNames.FileName"/> 与 <see cref="IndexFieldNames.Content"/>
/// 两个"可分词全文检索"字段。
///
/// 为什么不用 Lucene 自带的 StandardAnalyzer：StandardAnalyzer 对中文是按"单字"切分的
/// （逐字拆开，不识别词语边界），会导致中文短语搜索精度差、且倒排索引膨胀。使用 jieba 的
/// 搜索引擎分词模式后，"文件搜索软件"这样的连续中文可以被切成更贴近自然语义的词组
/// （文件/搜索/软件），同时保留细粒度子词，兼顾召回率与准确率。
///
/// jieba.NET 的 <see cref="JiebaSegmenter"/> 在首次使用时会从磁盘的 Resources/ 目录加载
/// 词典与 HMM 模型（体积不小，加载有一定耗时），因此这里做成单例，在整个进程生命周期内
/// 只加载一次、被所有索引/搜索操作共享，而不是每次创建 Analyzer 都重新构造分词器实例。
/// </summary>
public sealed class FileTraceAnalyzer : Analyzer
{
    /// <summary>
    /// 当前 Lucene.Net 4.8.0-beta 版本目标；索引与查询两端必须使用完全相同的 LuceneVersion，
    /// 否则某些分词组件的兼容性行为（例如 StopFilter 的语义版本开关）可能不一致。
    /// </summary>
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    // jieba 分词器进程内单例：词典/HMM 模型只加载一次，供所有 Analyzer 实例复用。
    private static readonly Lazy<JiebaSegmenter> SharedSegmenter = new(() => new JiebaSegmenter());

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new JiebaTokenizer(reader, SharedSegmenter.Value);
        TokenStream tokenStream = new LowerCaseFilter(MatchVersion, tokenizer);
        return new TokenStreamComponents(tokenizer, tokenStream);
    }
}
