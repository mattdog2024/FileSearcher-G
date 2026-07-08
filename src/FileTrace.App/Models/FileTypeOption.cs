using CommunityToolkit.Mvvm.ComponentModel;
using FileTrace.Core.Models;

namespace FileTrace.App.Models;

/// <summary>
/// "新建索引"对话框里一个可勾选的文件类型条目，包装 <see cref="FileTypeDescriptor"/>
/// 并附加一个可绑定的 IsChecked 状态，供 CheckBox 列表双向绑定。
/// </summary>
public sealed partial class FileTypeOption : ObservableObject
{
    public FileTypeOption(FileTypeDescriptor descriptor, bool isChecked = true)
    {
        Descriptor = descriptor;
        IsChecked = isChecked;
    }

    public FileTypeDescriptor Descriptor { get; }

    public string Extension => Descriptor.Extension;

    public string DisplayLabel => $".{Descriptor.Extension}";

    [ObservableProperty]
    private bool isChecked;
}
