using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AegiNext.Desktop.Settings;

/// <summary>颜色选择器与可持久化 RGB 字符串的局部呈现转换。</summary>
public sealed class SettingsAccentColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Color.Parse(value as string ?? "#5273E8");
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = (Color)value!;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
