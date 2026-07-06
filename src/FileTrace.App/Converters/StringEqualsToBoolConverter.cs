using System.Globalization;
using System.Windows.Data;

namespace FileTrace.App.Converters;

/// <summary>
/// 用于把一个字符串状态值（例如 IndexStatus 的字符串表示，或 Category 字符串）
/// 与 ConverterParameter 相比较，返回 bool，常见用途是驱动 RadioButton 风格的胶囊选中态。
/// </summary>
public sealed class StringEqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
