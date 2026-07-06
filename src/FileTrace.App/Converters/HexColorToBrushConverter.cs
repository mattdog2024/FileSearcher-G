using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FileTrace.App.Converters;

/// <summary>
/// 把 FileTypeCatalog 里定义的十六进制颜色字符串（例如 "#2563EB"）转换为 Brush，
/// 用于 ResultCard 左侧的文件类型色块。
/// </summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
                // 忽略非法颜色字符串，回退到默认色。
            }
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
