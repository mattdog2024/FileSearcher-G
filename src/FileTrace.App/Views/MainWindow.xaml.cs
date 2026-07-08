using System.Windows;

namespace FileTrace.App.Views;

/// <summary>
/// 主窗口代码隐藏。刻意保持"瘦身"——所有状态与交互逻辑都在 MainViewModel 里，
/// 这里只负责窗口本身的生命周期，不写业务逻辑。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
