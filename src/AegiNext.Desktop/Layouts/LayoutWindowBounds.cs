namespace AegiNext.Desktop.Layouts;

internal static class LayoutWindowBounds
{
    internal static LayoutFloatingSnapshot Clamp(LayoutFloatingSnapshot floating, IReadOnlyList<LayoutScreenArea> screens)
    {
        if (screens.Count == 0)
        {
            return floating;
        }

        var originalWidth = floating.Width * floating.Scaling;
        var originalHeight = floating.Height * floating.Scaling;
        var selected = screens.OrderByDescending(screen => IntersectionArea(floating.X, floating.Y, originalWidth, originalHeight, screen))
            .ThenBy(screen => Math.Pow(floating.X - screen.X, 2) + Math.Pow(floating.Y - screen.Y, 2))
            .First();
        var width = Math.Min(floating.Width, selected.Width / selected.Scaling);
        var height = Math.Min(floating.Height, selected.Height / selected.Scaling);
        return floating with
        {
            X = Math.Clamp(floating.X, selected.X, selected.X + selected.Width - width * selected.Scaling),
            Y = Math.Clamp(floating.Y, selected.Y, selected.Y + selected.Height - height * selected.Scaling),
            Width = width,
            Height = height,
            Scaling = selected.Scaling
        };
    }

    private static double IntersectionArea(double x, double y, double width, double height, LayoutScreenArea area)
    {
        return Math.Max(0, Math.Min(x + width, area.X + area.Width) - Math.Max(x, area.X))
            * Math.Max(0, Math.Min(y + height, area.Y + area.Height) - Math.Max(y, area.Y));
    }
}
