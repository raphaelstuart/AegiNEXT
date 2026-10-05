using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace AegiNext.Desktop.Styling;

internal static class WorkbenchTextFormatting
{
    internal static TextLayout CreateLayout(StyledElement owner, string text, double size, IBrush? foreground,
        double lineHeight = double.NaN, double maximumWidth = double.PositiveInfinity)
    {
        var family = owner.TryFindResource("WorkbenchBodyFontFamily", out var font) && font is FontFamily bodyFamily
            ? bodyFamily : FontFamily.Default;
        if (double.IsNaN(lineHeight) && owner.TryFindResource("WorkbenchInputLineHeight", out var height) && height is double bodyHeight)
        {
            lineHeight = bodyHeight;
        }
        return new(text, new Typeface(family), size, foreground, textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis, maxWidth: maximumWidth, lineHeight: lineHeight);
    }

    internal static Point CenteredOrigin(TextLayout layout, Rect rectangle) =>
        new(rectangle.X, rectangle.Y + Math.Max(0, (rectangle.Height - layout.Height) / 2));
}
