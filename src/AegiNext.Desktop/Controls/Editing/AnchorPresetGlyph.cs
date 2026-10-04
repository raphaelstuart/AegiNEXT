using AegiNext.Core.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

/// <summary>非拉伸预设的矢量示意；实点标记 anchor，空心环标记当前 pivot 对应的预设。</summary>
public sealed class AnchorPresetGlyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<AnchorPresetGlyph, IBrush?>(nameof(Foreground), Brushes.Gray);
    public static readonly StyledProperty<bool> IsAnchorSelectedProperty =
        AvaloniaProperty.Register<AnchorPresetGlyph, bool>(nameof(IsAnchorSelected));
    public static readonly StyledProperty<bool> IsPivotSelectedProperty =
        AvaloniaProperty.Register<AnchorPresetGlyph, bool>(nameof(IsPivotSelected));

    static AnchorPresetGlyph()
    {
        AffectsRender<AnchorPresetGlyph>(ForegroundProperty, IsAnchorSelectedProperty, IsPivotSelectedProperty);
    }

    public ScenePoint Anchor { get; init; }
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }
    public bool IsAnchorSelected
    {
        get => GetValue(IsAnchorSelectedProperty);
        set => SetValue(IsAnchorSelectedProperty, value);
    }
    public bool IsPivotSelected
    {
        get => GetValue(IsPivotSelectedProperty);
        set => SetValue(IsPivotSelectedProperty, value);
    }

    /// <summary>按照控件尺寸绘制父画布与单点锚点；没有拉伸边或矩形布局含义。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var canvas = new Rect(5, 5, Math.Max(1, Bounds.Width - 10), Math.Max(1, Bounds.Height - 10));
        var point = new Point(canvas.Left + canvas.Width * Anchor.X, canvas.Top + canvas.Height * Anchor.Y);
        var pen = new Pen(Foreground, IsAnchorSelected ? 2 : 1);
        context.DrawRectangle(null, pen, canvas);
        context.DrawEllipse(Foreground, null, point, 2.5, 2.5);
        if (IsPivotSelected)
        {
            context.DrawEllipse(null, new Pen(Foreground, 1.5), point, 5, 5);
        }
    }
}
