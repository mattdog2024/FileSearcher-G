using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace FileTrace.App.Views.Controls;

/// <summary>
/// 无限循环旋转的加载指示器，挂载/卸载时启动或停止动画，避免不可见时仍消耗 CPU 做动画计算。
/// </summary>
public partial class LoadingSpinner : UserControl
{
    private readonly DoubleAnimation _animation;

    public LoadingSpinner()
    {
        InitializeComponent();
        _animation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RingRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, _animation);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RingRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
    }
}
