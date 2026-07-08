using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FileTrace.App.Converters;

/// <summary>
/// 把一个字符串资源键（例如 IndexProfileCardViewModel.StatusBrushKey 返回的 "StatusOkBrush"）
/// 解析为实际的 Brush 资源。这样 ViewModel 只需要暴露语义化的字符串键，
/// 不需要引用 System.Windows.Media 也能驱动 UI 换色，保持 ViewModel 与 WPF 类型解耦。
/// </summary>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
