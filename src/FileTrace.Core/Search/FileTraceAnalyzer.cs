using System.IO;
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
/// <para>
/// 为什么不用 Lucene 自带的 StandardAnalyzer：StandardAnalyzer 对中文是按"单字"切分的
/// （逐字拆开，不识别词语边界），会导致中文短语搜索精度差、且倒排索引膨胀。使用 jieba 的
/// 搜索引擎分词模式后，"文件搜索软件"这样的连续中文可以被切成更贴近自然语义的词组
/// （文件/搜索/软件），同时保留细粒度子词，兼顾召回率与准确率。
/// </para>
///
/// <para>
/// 关于 jieba.NET 词典目录的定位：jieba.NET 默认按 <c>Resources/&lt;词典文件&gt;</c>
/// 的相对路径加载词典，基准是 <see cref="AppContext.BaseDirectory"/>。
/// 在 .NET 8 单文件发布场景下运行时基目录会指向 native 自解压临时目录，不会指向
/// FileTrace.exe 旁边；如果不修正，会触发本类最初线上报告的
/// <c>TypeInitializationException: Could not find file '...Resources\prob_emit.json'</c>。
/// 因此 <see cref="FileTraceAnalyzer"/> 在静态初始化阶段就调用
/// <see cref="JiebaResourceResolver.EnsureConfigured"/>，把 jieba.NET 的
/// <see cref="ConfigManager.ConfigFileBaseDir"/> 显式指向一个 **绝对路径**，
/// 从根上消除对 <see cref="AppContext.BaseDirectory"/> 的依赖。
/// </para>
///
/// <para>
/// 进程内仍然只构造一次 <see cref="JiebaSegmenter"/>：词典/HMM 模型只加载一次，
/// 供所有 <see cref="FileTraceAnalyzer"/> 实例复用，避免每个 Lucene writer/searcher
/// 都重新触发慢的 IO+JSON 解析。
/// </para>
/// </summary>
public sealed class FileTraceAnalyzer : Analyzer
{
    /// <summary>
    /// 当前 Lucene.Net 4.8.0-beta 版本目标；索引与查询两端必须使用完全相同的 LuceneVersion，
    /// 否则某些分词组件的兼容性行为（例如 StopFilter 的语义版本开关）可能不一致。
    /// </summary>
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    // 文件锁定：保护 SharedSegmenter 一次性初始化的可见性/原子性。
    private static readonly object _segmenterLock = new();
    private static JiebaSegmenter? _sharedSegmenter;

    static FileTraceAnalyzer()
    {
        // 必须抢在任何 new JiebaSegmenter() 之前把 ConfigManager.ConfigFileBaseDir
        // 指向绝对路径。CLR 的行为是：JiebaSegmenter..cctor() 一旦失败，永久缓存
        // 失败状态，后续即使再次设置 ConfigFileBaseDir 也救不回来。
        JiebaResourceResolver.EnsureConfigured();
    }

    /// <summary>
    /// 进程内共享的 <see cref="JiebaSegmenter"/> 单例。所有 <see cref="FileTraceAnalyzer"/>
    /// 实例复用同一个分词器实例，词典/HMM 模型只在进程生命周期内加载一次。
    /// </summary>
    public static JiebaSegmenter SharedSegmenter
    {
        get
        {
            if (_sharedSegmenter is not null)
            {
                return _sharedSegmenter;
            }

            lock (_segmenterLock)
            {
                if (_sharedSegmenter is null)
                {
                    try
                    {
                        _sharedSegmenter = new JiebaSegmenter();
                    }
                    catch (System.TypeInitializationException ex) when (
                        ex.InnerException is System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException
                        || JiebaResourceResolver.ResolvedRoot is null)
                    {
                        // 词典目录被改坏 / 被运行时误删，例如：
                        //   - 用户在索引过程中把 Resources 文件夹搬走/重命名；
                        //   - antivirus 把 json 当威胁隔离；
                        //   - 便携版被解压到了一个普通用户没有读权限的位置。
                        // 把 jieba.NET 内部的 TypeInitializationException 翻译成
                        // 一个明确指出"Resources/Prob_emit.json 之类文件找不到"
                        // 并附解析到的目录路径的 DirectoryNotFoundException，
                        // 让 IndexingCoordinator / Logger / UI 能给出可读的错误。
                        throw new DirectoryNotFoundException(
                            "jieba.NET 中文分词词典加载失败：" + ex.InnerException?.Message
                            + "。已配置的 jieba 词典目录："
                            + (JiebaResourceResolver.ResolvedRoot ?? "<未配置>") + "。"
                            + "请确认 FileTrace.exe 旁边的 Resources 文件夹完整、未被杀毒软件隔离，"
                            + "并重新启动程序。", ex);
                    }
                }
                return _sharedSegmenter;
            }
        }
    }

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
    {
        var tokenizer = new JiebaTokenizer(reader, SharedSegmenter);
        TokenStream tokenStream = new LowerCaseFilter(MatchVersion, tokenizer);
        return new TokenStreamComponents(tokenizer, tokenStream);
    }
}
