using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using JiebaNet.Segmenter;

namespace FileTrace.Core.Search;

/// <summary>
/// 把 jieba.NET 分词器包装成 Lucene.Net 的 <see cref="Tokenizer"/>。
///
/// 设计要点：
/// - 使用 jieba 的"搜索引擎模式"（<see cref="TokenizerMode.Search"/>）分词：该模式会在精确分词的基础上，
///   对长词再次切分出更细粒度的子词（例如"中华人民共和国"还会额外产出"中华""人民""共和国"等），
///   更适合检索场景下用户可能只输入部分词语的情况。
/// - jieba.NET 的 API 是一次性把整段文本切完返回 IEnumerable&lt;Token&gt;，不是增量式的流式接口，
///   因此这里在第一次调用 IncrementToken() 时，读取全部输入文本、执行一次分词，缓存结果后逐个吐出，
///   这是所有基于第三方"一次性分词库"实现 Lucene Tokenizer 的标准做法（Lucene 官方的
///   SmartChineseAnalyzer/IK Analyzer 等中文分词器也是类似模式）。
/// - jieba 返回的空白/标点 token（Word 为空白字符串）会被跳过，不进入索引，避免大量无意义的单字符
///   标点占用倒排索引空间、也避免用户搜索空格意外命中所有文档。
/// - 类型名 <c>Token</c> 在 Lucene.Net.Analysis 命名空间下同样存在（历史遗留的旧式 Token 类），
///   因此这里显式使用 JiebaNet.Segmenter.Token 的完全限定名，避免编译期产生二义性引用错误。
/// </summary>
public sealed class JiebaTokenizer : Tokenizer
{
    private readonly ICharTermAttribute _termAttribute;
    private readonly IOffsetAttribute _offsetAttribute;
    private readonly JiebaSegmenter _segmenter;

    private IEnumerator<JiebaNet.Segmenter.Token>? _pendingTokens;
    private int _finalOffset;

    public JiebaTokenizer(TextReader input, JiebaSegmenter segmenter)
        : base(input)
    {
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _termAttribute = AddAttribute<ICharTermAttribute>();
        _offsetAttribute = AddAttribute<IOffsetAttribute>();
    }

    public override bool IncrementToken()
    {
        ClearAttributes();

        if (_pendingTokens is null)
        {
            string text = m_input.ReadToEnd();
            _finalOffset = text.Length;
            _pendingTokens = _segmenter
                .Tokenize(text, TokenizerMode.Search, hmm: true)
                .GetEnumerator();
        }

        // 跳过 jieba 切出的纯空白/标点 token，直到拿到一个有效词或耗尽为止。
        while (_pendingTokens.MoveNext())
        {
            var token = _pendingTokens.Current;
            if (string.IsNullOrWhiteSpace(token.Word))
            {
                continue;
            }

            _termAttribute.Append(token.Word);
            _offsetAttribute.SetOffset(
                CorrectOffset(token.StartIndex),
                CorrectOffset(token.EndIndex));
            return true;
        }

        return false;
    }

    public override void End()
    {
        base.End();
        int correctedFinalOffset = CorrectOffset(_finalOffset);
        _offsetAttribute.SetOffset(correctedFinalOffset, correctedFinalOffset);
    }

    public override void Reset()
    {
        base.Reset();
        _pendingTokens = null;
    }
}
