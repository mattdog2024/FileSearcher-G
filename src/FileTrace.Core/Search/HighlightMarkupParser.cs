namespace FileTrace.Core.Search;

/// <summary>
/// 高亮文本中的一个片段：普通文本或命中关键词的高亮文本。
/// </summary>
public readonly record struct HighlightSegment(string Text, bool IsHighlighted);

/// <summary>
/// 将 <see cref="SearchService"/> 通过 Lucene.Net.Highlighter 生成的
/// "<mark>关键词</mark>普通文本..." 格式的高亮片段字符串解析为结构化片段列表。
///
/// 之所以把这个解析逻辑放在 FileTrace.Core（而不是 WPF 项目里的代码隐藏文件中），
/// 是为了让它保持与具体 UI 框架无关、可被 xUnit 直接单元测试；
/// WPF 层只需要把解析结果渲染成 TextBlock.Inlines 里的 Run 即可（一个薄的 UI 适配层）。
/// </summary>
public static class HighlightMarkupParser
{
    private const string OpenTag = "<mark>";
    private const string CloseTag = "</mark>";

    /// <summary>
    /// 解析高亮标记字符串。对不完整/缺失闭合标签的异常输入采取宽松容错处理，
    /// 保证任何输入都不会抛异常（搜索结果渲染不应因为个别脏数据崩溃）。
    /// </summary>
    public static IReadOnlyList<HighlightSegment> Parse(string? markup)
    {
        var segments = new List<HighlightSegment>();
        if (string.IsNullOrEmpty(markup))
        {
            return segments;
        }

        int cursor = 0;
        while (cursor < markup.Length)
        {
            int openIdx = markup.IndexOf(OpenTag, cursor, StringComparison.Ordinal);
            if (openIdx < 0)
            {
                segments.Add(new HighlightSegment(markup[cursor..], false));
                break;
            }

            if (openIdx > cursor)
            {
                segments.Add(new HighlightSegment(markup[cursor..openIdx], false));
            }

            int contentStart = openIdx + OpenTag.Length;
            int closeIdx = markup.IndexOf(CloseTag, contentStart, StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                // 容错：没有找到闭合标签，把剩余内容原样当作普通文本输出，不再当作高亮处理。
                segments.Add(new HighlightSegment(markup[contentStart..], false));
                break;
            }

            segments.Add(new HighlightSegment(markup[contentStart..closeIdx], true));
            cursor = closeIdx + CloseTag.Length;
        }

        return segments;
    }
}
