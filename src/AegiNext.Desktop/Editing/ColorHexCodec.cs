using System.Globalization;
using AegiNext.Core.Projects;
using Avalonia.Media;

namespace AegiNext.Desktop.Editing;

/// <summary>明确使用 CSS 顺序的 sRGB HEX 编解码；工程继续保存线性色。</summary>
public static class ColorHexCodec
{
    /// <summary>读取 #RRGGBB 或 #RRGGBBAA；六位不透明，关闭 Alpha 时保留既有透明度。</summary>
    public static bool TryParse(string text, double previousAlpha, bool alphaEnabled, out SceneColor value)
    {
        value = default;
        var token = text.Trim();
        if (token.Length is not (7 or 9) || token[0] != '#' ||
            !uint.TryParse(token.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        var withAlpha = token.Length == 9;
        var red = (byte)(packed >> (withAlpha ? 24 : 16));
        var green = (byte)(packed >> (withAlpha ? 16 : 8));
        var blue = (byte)(packed >> (withAlpha ? 8 : 0));
        var color = SceneColorConversion.FromColor(Color.FromArgb(255, red, green, blue));
        value = color with { Alpha = alphaEnabled ? withAlpha ? (byte)packed / 255d : 1 : previousAlpha };
        return true;
    }

    /// <summary>将线性色映射成 sRGB HEX，仅用于显示，不写回或截断工程值。</summary>
    public static string Format(SceneColor value, bool includeAlpha)
    {
        var color = SceneColorConversion.ToColor(value);
        return includeAlpha ? $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}" : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
