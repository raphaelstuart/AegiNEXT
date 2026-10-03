using Avalonia.Media;
using AegiNext.Core.Projects;
using AegiNext.Rendering;

namespace AegiNext.Desktop.Editing;

internal static class SceneColorConversion
{
    internal static SceneColor FromColor(Color value)
    {
        var linear = LinearColor.FromSrgb(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
        return new(linear.Red, linear.Green, linear.Blue, linear.Alpha);
    }

    internal static Color ToColor(SceneColor value)
    {
        return Color.FromArgb((byte)Math.Clamp(Math.Round(value.Alpha * 255), 0, 255), Encode(value.Red), Encode(value.Green), Encode(value.Blue));
    }

    private static byte Encode(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var encoded = value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }
}
