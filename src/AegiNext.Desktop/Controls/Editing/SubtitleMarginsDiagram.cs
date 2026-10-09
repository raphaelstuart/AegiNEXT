using AegiNext.Core.Projects;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ProjectTextAlignment = AegiNext.Core.Projects.TextAlignment;

namespace AegiNext.Desktop.Controls;

/// <summary>三轴边距的只读布局示意；标记表示行框，不测量或推断字形边界。</summary>
public sealed class SubtitleMarginsDiagram : Control
{
    private const double PADDING = 10;
    private const double MARKER_HEIGHT = 8;
    public static readonly StyledProperty<SubtitleMargins?> MarginsProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, SubtitleMargins?>(nameof(Margins));
    public static readonly StyledProperty<int> AlignmentProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, int>(nameof(Alignment), 7);
    public static readonly StyledProperty<bool> IsExplicitProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, bool>(nameof(IsExplicit));
    public static readonly StyledProperty<int> CanvasWidthProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, int>(nameof(CanvasWidth), 1920);
    public static readonly StyledProperty<int> CanvasHeightProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, int>(nameof(CanvasHeight), 1080);
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SubtitleMarginsDiagram>();
    public static readonly StyledProperty<IBrush?> CanvasBrushProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, IBrush?>(nameof(CanvasBrush));
    public static readonly StyledProperty<IBrush?> MarginBrushProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, IBrush?>(nameof(MarginBrush));
    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<SubtitleMarginsDiagram, IBrush?>(nameof(MarkerBrush));

    static SubtitleMarginsDiagram()
    {
        AffectsRender<SubtitleMarginsDiagram>(MarginsProperty, AlignmentProperty, IsExplicitProperty,
            CanvasWidthProperty, CanvasHeightProperty, ForegroundProperty, CanvasBrushProperty, MarginBrushProperty, MarkerBrushProperty);
    }

    /// <summary>禁用焦点和输入命中，示意图不参与编辑事务。</summary>
    public SubtitleMarginsDiagram()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public SubtitleMargins? Margins
    {
        get => GetValue(MarginsProperty);
        set => SetValue(MarginsProperty, value);
    }
    public int Alignment
    {
        get => GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }
    public bool IsExplicit
    {
        get => GetValue(IsExplicitProperty);
        set => SetValue(IsExplicitProperty, value);
    }
    public int CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }
    public int CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }
    public IBrush? CanvasBrush
    {
        get => GetValue(CanvasBrushProperty);
        set => SetValue(CanvasBrushProperty, value);
    }
    public IBrush? MarginBrush
    {
        get => GetValue(MarginBrushProperty);
        set => SetValue(MarginBrushProperty, value);
    }
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>等比例绘制画布留白与自动行框位置；无效草稿只显示画布轮廓。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (CanvasWidth <= 0 || CanvasHeight <= 0 || Bounds.Width <= PADDING * 2 || Bounds.Height <= PADDING * 2)
        {
            return;
        }
        var scale = Math.Min((Bounds.Width - PADDING * 2) / CanvasWidth, (Bounds.Height - PADDING * 2) / CanvasHeight);
        var size = new Size(CanvasWidth * scale, CanvasHeight * scale);
        var canvas = new Rect(Bounds.Size.Width / 2 - size.Width / 2, Bounds.Size.Height / 2 - size.Height / 2, size.Width, size.Height);
        var pen = new Pen(Foreground, 1);
        context.DrawRectangle(CanvasBrush, pen, canvas);
        if (Margins is not { } margins || !IsValid(margins.Left) || !IsValid(margins.Right) || !IsValid(margins.Vertical) ||
            !Enum.IsDefined((ProjectTextAlignment)Alignment))
        {
            return;
        }

        var left = canvas.Left + Math.Min(margins.Left * scale, canvas.Width);
        var right = Math.Max(left, canvas.Right - Math.Min(margins.Right * scale, canvas.Width));
        var inner = new Rect(left, canvas.Top, right - left, canvas.Height);
        using (context.PushOpacity(0.18))
        {
            context.DrawRectangle(MarginBrush, null, new Rect(canvas.Left, canvas.Top, left - canvas.Left, canvas.Height));
            context.DrawRectangle(MarginBrush, null, new Rect(right, canvas.Top, canvas.Right - right, canvas.Height));
        }
        context.DrawLine(pen, new(left, canvas.Top), new(left, canvas.Bottom));
        context.DrawLine(pen, new(right, canvas.Top), new(right, canvas.Bottom));
        DrawLabel(context, "L", new(canvas.Left - 6, canvas.Center.Y));
        DrawLabel(context, "R", new(canvas.Right + 6, canvas.Center.Y));
        if (IsExplicit || inner.Width <= 0)
        {
            return;
        }

        var row = Alignment / 3;
        var vertical = Math.Min(margins.Vertical * scale, canvas.Height);
        var edge = row switch
        {
            0 => canvas.Top + vertical,
            2 => canvas.Bottom - vertical,
            _ => canvas.Center.Y
        };
        if (row != 1)
        {
            var band = row == 0
                ? new Rect(inner.Left, canvas.Top, inner.Width, vertical)
                : new Rect(inner.Left, edge, inner.Width, vertical);
            using (context.PushOpacity(0.18))
            {
                context.DrawRectangle(MarginBrush, null, band);
            }
            context.DrawLine(pen, new(inner.Left, edge), new(inner.Right, edge));
            DrawLabel(context, "V", new(canvas.Center.X, row == 0 ? canvas.Top - 6 : canvas.Bottom + 6));
        }
        var width = Math.Min(inner.Width, 44);
        var x = (Alignment % 3) switch
        {
            0 => inner.Left,
            2 => inner.Right - width,
            _ => inner.Center.X - width / 2
        };
        var y = row switch
        {
            0 => edge,
            2 => edge - MARKER_HEIGHT,
            _ => edge - MARKER_HEIGHT / 2
        };
        var marker = new Rect(x, y, width, MARKER_HEIGHT);
        using (context.PushOpacity(0.25))
        {
            context.DrawRectangle(MarkerBrush, null, marker);
        }
        context.DrawRectangle(null, new Pen(MarkerBrush, 1.5), marker);
    }

    private static bool IsValid(double value)
    {
        return double.IsFinite(value) && value is >= 0 and <= 32768;
    }

    private void DrawLabel(DrawingContext context, string text, Point center)
    {
        using var layout = WorkbenchTextFormatting.CreateLayout(this, text, 9, Foreground, 10);
        var x = Math.Clamp(center.X - layout.Width / 2, 0, Math.Max(0, Bounds.Width - layout.Width));
        var y = Math.Clamp(center.Y - layout.Height / 2, 0, Math.Max(0, Bounds.Height - layout.Height));
        layout.Draw(context, new(x, y));
    }
}
