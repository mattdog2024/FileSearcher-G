using FileTrace.Core.Models;
using FileTrace.Core.Search;

namespace FileTrace.App.ViewModels;

/// <summary>
/// 一条搜索结果的视图模型，包装 <see cref="SearchResultItem"/> 并把 Lucene 高亮标记字符串
/// 预解析为结构化的 <see cref="HighlightSegment"/> 列表，供 XAML 里的附加行为
/// （见 Behaviors/HighlightTextBehavior）渲染为带高亮底色的 Run 集合。
/// 对应设计稿 ResultCard。
/// </summary>
public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(SearchResultItem item)
    {
        Item = item;
        FileNameSegments = HighlightMarkupParser.Parse(item.FileNameHighlight ?? item.FileName);
        ContentSegments = HighlightMarkupParser.Parse(item.ContentHighlight);
    }

    public SearchResultItem Item { get; }

    public string FileName => Item.FileName;

    public string DirectoryPath => Item.DirectoryPath;

    public string FullPath => Item.FullPath;

    public string ExtensionNoDot => Item.ExtensionNoDot;

    public long SizeBytes => Item.SizeBytes;

    public DateTimeOffset LastWriteTimeUtc => Item.LastWriteTimeUtc;

    public bool ContentIndexed => Item.ContentIndexed;

    public float Score => Item.Score;

    public IReadOnlyList<HighlightSegment> FileNameSegments { get; }

    public IReadOnlyList<HighlightSegment> ContentSegments { get; }

    /// <summary>没有内容片段时（PPT方案C的纯文件名文档，或无内容命中），UI 用这句提示代替内容预览区。</summary>
    public bool HasContentPreview => ContentSegments.Count > 0;

    public string NoPreviewHint => ContentIndexed
        ? "未匹配到内容片段"
        : "此文件类型暂不支持内容预览（仅按文件名索引）";

    public FileTypeDescriptor? TypeDescriptor => FileTypeCatalog.Find(ExtensionNoDot);

    public string TypeIconLabel => TypeDescriptor?.IconLabel ?? ExtensionNoDot.ToUpperInvariant();

    public string TypeIconColorHex => TypeDescriptor?.IconColorHex ?? "#64748B";

    public string SizeDisplay => FormatBytes(SizeBytes);

    public string LastWriteDisplay => LastWriteTimeUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:N0} {units[unitIndex]}"
            : $"{size:N1} {units[unitIndex]}";
    }
}
