using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTrace.Core.Models;

namespace FileTrace.App.ViewModels;

/// <summary>
/// "新建索引"对话框的视图模型，对应设计稿新建索引弹窗：
/// 选择根目录 → 选择索引存储位置 → 勾选文件类型 → 是否增量/OCR → 确认创建。
///
/// Stage2 阶段：表单校验与 UI 交互完整可用；"浏览文件夹"用 Stage3 会替换为真实的
/// Win32 文件夹选择对话框（Microsoft.Win32 / WPF 没有内置的文件夹选择器，
/// 常见做法是用 OpenFolderDialog(.NET 8 新增) 或 Win32 互操作），这里先用占位命令，
/// 允许用户直接在文本框中粘贴/输入路径来跑通表单其余逻辑与校验规则。
/// </summary>
public sealed partial class NewIndexDialogViewModel : ObservableObject
{
    public NewIndexDialogViewModel()
    {
        FileTypes = new List<FileTrace.App.Models.FileTypeOption>(
            FileTypeCatalog.All.Select(d => new FileTrace.App.Models.FileTypeOption(d, isChecked: true)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string rootPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string storagePath = string.Empty;

    [ObservableProperty]
    private bool incrementalEnabled = true;

    [ObservableProperty]
    private bool ocrEnabled = false;

    [ObservableProperty]
    private string maxFileSizeMbText = "200";

    public List<FileTrace.App.Models.FileTypeOption> FileTypes { get; }

    public bool CanConfirm =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(RootPath)
        && !string.IsNullOrWhiteSpace(StoragePath)
        && FileTypes.Any(t => t.IsChecked);

    [RelayCommand]
    private void SelectAllTypes()
    {
        foreach (var t in FileTypes)
        {
            t.IsChecked = true;
        }
    }

    [RelayCommand]
    private void ClearAllTypes()
    {
        foreach (var t in FileTypes)
        {
            t.IsChecked = false;
        }
    }

    /// <summary>把当前表单状态构建为一个待创建的 <see cref="IndexProfile"/>。供确认按钮的命令处理器调用。</summary>
    public IndexProfile BuildProfile()
    {
        long maxBytes = long.TryParse(MaxFileSizeMbText, out var mb) && mb > 0
            ? mb * 1024 * 1024
            : 200L * 1024 * 1024;

        return new IndexProfile
        {
            Name = Name.Trim(),
            RootPath = RootPath.Trim(),
            StoragePath = StoragePath.Trim(),
            IncludedExtensions = new HashSet<string>(
                FileTypes.Where(t => t.IsChecked).Select(t => t.Extension),
                StringComparer.OrdinalIgnoreCase),
            IncrementalEnabled = IncrementalEnabled,
            OcrEnabled = OcrEnabled,
            MaxFileSizeForContentBytes = maxBytes,
            Status = IndexStatus.Building,
        };
    }
}
