using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

internal static class AudioGraphColorRamp
{
    internal static Color[] Create(AudioGraphPalette palette)
    {
        palette.Validate();
        var low = Parse(palette.Low);
        var mid = Parse(palette.Mid);
        var high = Parse(palette.High);
        var result = new Color[256];
        for (var level = 0; level < result.Length; level++)
        {
            var intensity = level / 255d;
            if (palette.UseClassicSpectrum)
            {
                result[level] = Color.FromRgb((byte)Math.Clamp(18 + Math.Pow(intensity, 3) * 237, 0, 255),
                    (byte)Math.Clamp(28 + intensity * intensity * 220, 0, 255),
                    (byte)Math.Clamp(22 + 150 * Math.Sin(intensity * Math.PI), 0, 255));
            }
            else
            {
                var from = intensity <= 0.5 ? low : mid;
                var to = intensity <= 0.5 ? mid : high;
                var phase = intensity <= 0.5 ? intensity * 2 : (intensity - 0.5) * 2;
                result[level] = Color.FromRgb(Blend(from.R, to.R, phase), Blend(from.G, to.G, phase), Blend(from.B, to.B, phase));
            }
        }

        return result;
    }

    internal static Color Parse(string text)
    {
        if (!ColorHexCodec.TryParse(text, 1, true, out var color))
        {
            throw new InvalidDataException("音频图颜色无效。");
        }

        return SceneColorConversion.ToColor(color);
    }

    private static byte Blend(byte from, byte to, double phase) => (byte)Math.Round(from + (to - from) * phase);
}
