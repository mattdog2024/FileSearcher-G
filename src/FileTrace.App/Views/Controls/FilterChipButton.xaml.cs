using System.Windows;
using System.Windows.Controls;
using FileTrace.App.ViewModels;

namespace FileTrace.App.Views.Controls;

/// <summary>
/// 一枚类型筛选胶囊。点击后把自身 DataContext（FilterChipViewModel）的 IsSelected 置为 true；
/// MainViewModel 订阅了每个 chip 的 PropertyChanged，会负责把同组的其它 chip 取消选中
/// （单选语义），所以这里的点击处理不需要关心互斥逻辑。
/// </summary>
public partial class FilterChipButton : UserControl
{
    public FilterChipButton()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is FilterChipViewModel chip)
        {
            chip.IsSelected = true;
        }
    }
}
