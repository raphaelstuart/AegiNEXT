using System.Globalization;
using AegiNext.Core.Projects;
using Avalonia.Media;

namespace AegiNext.Desktop.Editing;

/// <summary>逗号分隔的四个 sRGB 字节与工程线性色之间的转换。</summary>
public static class ColorRgbaCodec
{
    /// <summary>读取四个 0–255 整数；关闭 Alpha 时保留工程透明度。</summary>
    public static bool TryParse(string text, double previousAlpha, bool alphaEnabled, out SceneColor value)
    {
        value = default;
        var parts = text.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        var components = new byte[4];
        for (var index = 0; index < components.Length; index++)
        {
            if (!byte.TryParse(parts[index].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out components[index]))
            {
                return false;
            }
        }

        var color = SceneColorConversion.FromColor(Color.FromArgb(255, components[0], components[1], components[2]));
        value = color with { Alpha = alphaEnabled ? components[3] / 255d : previousAlpha };
        return true;
    }

    /// <summary>将线性色投影为 sRGB 字节；格式化本身不修改 HDR 工程值。</summary>
    public static string Format(SceneColor value)
    {
        var color = SceneColorConversion.ToColor(value);
        return $"{color.R},{color.G},{color.B},{color.A}";
    }
}
