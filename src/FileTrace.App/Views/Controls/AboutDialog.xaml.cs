using System.Windows.Controls;

namespace FileTrace.App.Views.Controls;

/// <summary>
/// "关于"对话框：纯展示 + 两个简单命令（打开日志文件夹 / 关闭），不需要像
/// NewIndexDialog 那样回调 MainViewModel 上的命令，所有交互都封装在
/// <see cref="FileTrace.App.ViewModels.AboutDialogViewModel"/> 自身，
/// 因此这里不需要额外的事件转发代码，是本项目里对话框实现最简单的一个。
/// </summary>
public partial class AboutDialog : UserControl
{
    public AboutDialog()
    {
        InitializeComponent();
    }
}
