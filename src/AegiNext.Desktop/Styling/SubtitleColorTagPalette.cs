using Avalonia.Media;

namespace AegiNext.Desktop.Styling;

internal static class SubtitleColorTagPalette
{
    internal static SolidColorBrush ResolveSwatch(string colorHex) => new(Color.Parse(colorHex));

    internal static SolidColorBrush ResolveRowBackground(string colorHex, bool dark, bool selected) =>
        WithAlpha(colorHex, dark ? selected ? (byte)56 : (byte)31 : selected ? (byte)51 : (byte)26);

    internal static SolidColorBrush ResolveClipBackground(string colorHex, bool dark, bool selected) =>
        WithAlpha(colorHex, dark ? selected ? (byte)97 : (byte)66 : selected ? (byte)102 : (byte)64);

    private static SolidColorBrush WithAlpha(string colorHex, byte alpha)
    {
        var color = Color.Parse(colorHex);
        return new(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
