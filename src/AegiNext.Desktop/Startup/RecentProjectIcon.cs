using System.Globalization;
using System.Text;
using Avalonia.Media;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectIcon
{
    private const uint HASH_OFFSET = 2166136261;
    private const uint HASH_PRIME = 16777619;
    private const double SATURATION = 0.56;
    private const double LIGHTNESS = 0.47;

    internal RecentProjectIcon(string name, string path)
    {
        Initials = GetInitials(name);
        var identity = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var hash = HASH_OFFSET;
        foreach (var value in Encoding.UTF8.GetBytes(identity))
        {
            hash = unchecked((hash ^ value) * HASH_PRIME);
        }

        var color = new HslColor(1, hash % 360, SATURATION, LIGHTNESS).ToRgb();
        Background = new SolidColorBrush(color);
        var luminance = 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
        Foreground = luminance > 0.179 ? Brushes.Black : Brushes.White;
    }

    internal string Initials { get; }
    internal SolidColorBrush Background { get; }
    internal IBrush Foreground { get; }

    private static double Linearize(byte component)
    {
        var value = component / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static string GetInitials(string name)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(name.Trim());
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        var initials = new List<string>(2);
        var characters = new List<string>(2);
        var startsWord = true;
        Rune? previous = null;
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var current = Rune.GetRuneAt(element, 0);
            if (!Rune.IsLetterOrDigit(current))
            {
                startsWord = true;
                previous = null;
                continue;
            }

            if (characters.Count < 2)
            {
                characters.Add(element);
            }

            var nextIsLower = index + 1 < elements.Count && Rune.IsLower(Rune.GetRuneAt(elements[index + 1], 0));
            var camelBoundary = previous is { } prior && Rune.IsUpper(current) && (Rune.IsLower(prior) || nextIsLower);
            if ((startsWord || camelBoundary) && initials.Count < 2)
            {
                initials.Add(element);
            }

            startsWord = false;
            previous = current;
        }

        if (characters.Count == 0)
        {
            return elements.Count == 0 ? "?" : string.Concat(elements.Take(2));
        }
        return string.Concat(initials.Count >= 2 ? initials : characters).ToUpperInvariant();
    }
}
