using CommunityToolkit.Mvvm.ComponentModel;

namespace FileTrace.App.ViewModels;

/// <summary>
/// 搜索结果上方"类型筛选行"（FilterRow）里的一枚筛选胶囊，例如"全部""文档""表格""PDF""代码"。
/// 选中时用于在 MainViewModel 里按 Category 对结果做客户端过滤（Stage2 先做纯 UI 交互，
/// Stage3 再决定是提交到 SearchService 的查询语法还是客户端二次过滤，两者互不冲突）。
/// </summary>
public sealed partial class FilterChipViewModel : ObservableObject
{
    public FilterChipViewModel(string category, string displayLabel, bool isSelected = false)
    {
        Category = category;
        DisplayLabel = displayLabel;
        this.isSelected = isSelected;
    }

    public string Category { get; }

    public string DisplayLabel { get; }

    [ObservableProperty]
    private bool isSelected;
}
