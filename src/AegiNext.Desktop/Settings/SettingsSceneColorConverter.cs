using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AegiNext.Desktop.Settings;

/// <summary>样式页面的显示色转换，仅显式颜色输入写回线性色。</summary>
public sealed class SettingsSceneColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return SceneColorConversion.ToColor((SceneColor)value!);
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return SceneColorConversion.FromColor((Color)value!);
    }
}
