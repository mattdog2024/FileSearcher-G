using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FileTrace.Core.Search;

namespace FileTrace.App.Behaviors;

/// <summary>
/// 附加属性：把一组 <see cref="HighlightSegment"/>（来自 SearchResultItemViewModel 预解析的高亮片段）
/// 渲染为 TextBlock.Inlines 里的 Run 集合，命中片段使用 HighlightBackgroundBrush/HighlightTextBrush 着色。
///
/// 用附加行为而不是在 XAML 里直接绑定字符串，是因为 TextBlock.Text 是纯文本属性，
/// 无法表达"部分文字高亮底色"这种富文本效果；Inlines 集合可以，但不能直接数据绑定，
/// 所以需要一个附加属性在绑定值变化时手动重建 Inlines。
/// </summary>
public static class HighlightTextBehavior
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IEnumerable),
            typeof(HighlightTextBehavior),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject element, IEnumerable? value) =>
        element.SetValue(SegmentsProperty, value);

    public static IEnumerable? GetSegments(DependencyObject element) =>
        (IEnumerable?)element.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        if (e.NewValue is not IEnumerable segments)
        {
            return;
        }

        var highlightBg = TryFindBrush(textBlock, "HighlightBackgroundBrush") ?? Brushes.Yellow;
        var highlightFg = TryFindBrush(textBlock, "HighlightTextBrush") ?? Brushes.Black;

        bool any = false;
        foreach (var obj in segments)
        {
            if (obj is not HighlightSegment segment)
            {
                continue;
            }

            any = true;
            var run = new Run(segment.Text);
            if (segment.IsHighlighted)
            {
                run.Background = highlightBg;
                run.Foreground = highlightFg;
                run.FontWeight = FontWeights.SemiBold;
            }

            textBlock.Inlines.Add(run);
        }

        if (!any)
        {
            // 没有任何片段（例如内容为空）时不渲染任何 Run，让上层的空态提示区块负责展示。
        }
    }

    private static Brush? TryFindBrush(FrameworkElement element, string resourceKey)
        => element.TryFindResource(resourceKey) as Brush;
}
