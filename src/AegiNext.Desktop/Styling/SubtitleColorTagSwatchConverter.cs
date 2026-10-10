using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AegiNext.Desktop.Styling;

/// <summary>将工程或个人标签的 RGB 值转换成主题无关的颜色圆点。</summary>
public sealed class SubtitleColorTagSwatchConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string colorHex ? SubtitleColorTagPalette.ResolveSwatch(colorHex) : Brushes.Transparent;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
