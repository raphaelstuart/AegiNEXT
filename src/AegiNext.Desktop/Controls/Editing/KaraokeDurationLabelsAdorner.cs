using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

internal sealed class KaraokeDurationLabelsAdorner : Control
{
    private IReadOnlyList<KaraokeDurationLabel> labels = [];

    internal KaraokeDurationLabelsAdorner()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = false;
    }

    internal void SetLabels(IReadOnlyList<KaraokeDurationLabel> value)
    {
        labels = value;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = this.TryFindResource("PreviewSurface", out var surface) && surface is IBrush brush ? brush : Brushes.Black;
        var foreground = this.TryFindResource("PreviewMuted", out var ink) && ink is IBrush inkBrush ? inkBrush : Brushes.White;
        var border = this.TryFindResource("PreviewBorder", out var edge) && edge is IBrush edgeBrush ? edgeBrush : Brushes.Gray;
        foreach (var label in labels)
        {
            context.DrawRectangle(background, new Pen(border), label.Bounds, 3, 3);
            using var text = WorkbenchTextFormatting.CreateLayout(this, label.Text, 11, foreground, lineHeight: 20);
            text.Draw(context, WorkbenchTextFormatting.CenteredOrigin(text,
                new(label.Bounds.X + 4, label.Bounds.Y, label.Bounds.Width - 8, label.Bounds.Height)));
        }
    }
}
