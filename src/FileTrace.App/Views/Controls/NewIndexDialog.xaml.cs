using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileTrace.App.ViewModels;

namespace FileTrace.App.Views.Controls;

/// <summary>
/// "新建索引"对话框。DataContext 是 <see cref="NewIndexDialogViewModel"/>（表单本身的状态），
/// 但"取消/确认创建"两个动作实际上属于 <see cref="MainViewModel"/>（负责把对话框结果
/// 落地为一个新的 IndexProfile 并加入左侧列表、关闭遮罩层）。
///
/// Stage2 阶段选择最简单直接的做法：通过 Application.Current.MainWindow.DataContext
/// 拿到 MainViewModel 引用后直接调用对应 RelayCommand。这是一个刻意的、局部的例外——
/// 更"纯粹"的 MVVM 做法是引入一个对话框服务(IDialogService)或消息总线，
/// 但 Stage2 的目标是先把交互骨架跑通，过度设计对话框基础设施会拖慢进度；
/// 如果 Stage3/4 需要支持多个层叠对话框或独立对话框窗口，再重构为专门的 DialogService。
///
/// "浏览…"按钮：使用 .NET 8 WPF 新增的 <see cref="Microsoft.Win32.OpenFolderDialog"/>
/// 原生文件夹选择对话框（无需第三方依赖）。本项目在 Linux 沙箱内开发，无法弹出真实
/// Windows 对话框做交互验证，实际弹窗效果与焦点行为需要在 Windows 环境二次确认；
/// 但该 API 属于 WPF 官方内置能力，编译期类型解析已在沙箱内通过验证。
/// </summary>
public partial class NewIndexDialog : UserControl
{
    public NewIndexDialog()
    {
        InitializeComponent();
    }

    private MainViewModel? MainViewModel => Application.Current.MainWindow?.DataContext as MainViewModel;

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        MainViewModel?.CancelNewIndexDialogCommand.Execute(null);
    }

    private async void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (MainViewModel is { } vm && vm.ConfirmNewIndexDialogCommand.CanExecute(null))
        {
            await vm.ConfirmNewIndexDialogCommand.ExecuteAsync(null);
        }
    }

    private void OnSelectAllTypes(object sender, MouseButtonEventArgs e)
    {
        (DataContext as NewIndexDialogViewModel)?.SelectAllTypesCommand.Execute(null);
    }

    private void OnClearAllTypes(object sender, MouseButtonEventArgs e)
    {
        (DataContext as NewIndexDialogViewModel)?.ClearAllTypesCommand.Execute(null);
    }

    private void OnBrowseRootPath(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewIndexDialogViewModel vm)
        {
            return;
        }

        var path = PickFolder("选择要建立索引的文件夹");
        if (!string.IsNullOrEmpty(path))
        {
            vm.RootPath = path;
        }
    }

    private void OnBrowseStoragePath(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewIndexDialogViewModel vm)
        {
            return;
        }

        var path = PickFolder("选择索引数据的存储位置");
        if (!string.IsNullOrEmpty(path))
        {
            vm.StoragePath = path;
        }
    }

    private static string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
