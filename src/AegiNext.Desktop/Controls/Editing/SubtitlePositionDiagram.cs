using AegiNext.Desktop.Styling;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

/// <summary>纯测量数据的只读位置示意：父画布、自然字形边界、anchor、pivot 和像素偏移。</summary>
public sealed class SubtitlePositionDiagram : Control
{
    private const double PADDING = 12;
    private const double LEGEND_HEIGHT = 20;
    public static readonly StyledProperty<SubtitlePositionGeometry?> GeometryProperty =
        AvaloniaProperty.Register<SubtitlePositionDiagram, SubtitlePositionGeometry?>(nameof(Geometry));
    public static readonly StyledProperty<SubtitlePosition?> PositionProperty =
        AvaloniaProperty.Register<SubtitlePositionDiagram, SubtitlePosition?>(nameof(Position));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SubtitlePositionDiagram>();

    static SubtitlePositionDiagram()
    {
        AffectsRender<SubtitlePositionDiagram>(GeometryProperty, PositionProperty, ForegroundProperty);
    }

    /// <summary>禁用输入命中；示意不创建任何编辑事务。</summary>
    public SubtitlePositionDiagram()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public SubtitlePositionGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }
    public SubtitlePosition? Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>按父局部坐标等比例绘制测量示意；超出画布的字形和偏移仍完整可见。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Geometry is not { } geometry || Position is not { } position ||
            geometry.ParentSize.X <= 0 || geometry.ParentSize.Y <= 0 ||
            Bounds.Width <= PADDING * 2 || Bounds.Height <= PADDING * 2 + LEGEND_HEIGHT)
        {
            return;
        }

        var anchor = new Point(position.Anchor.X * geometry.ParentSize.X, position.Anchor.Y * geometry.ParentSize.Y);
        var offset = anchor + new Vector(position.Offset.X, position.Offset.Y);
        var pivot = offset + new Vector(geometry.Transform.X, geometry.Transform.Y);
        var localPivot = new Point(geometry.GlyphOrigin.X + position.Pivot.X * geometry.GlyphSize.X + geometry.Transform.AnchorX,
            geometry.GlyphOrigin.Y + position.Pivot.Y * geometry.GlyphSize.Y + geometry.Transform.AnchorY);
        var radians = geometry.Transform.Rotation * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var glyph = new[]
        {
            MapGlyph(geometry.GlyphOrigin.X, geometry.GlyphOrigin.Y),
            MapGlyph(geometry.GlyphOrigin.X + geometry.GlyphSize.X, geometry.GlyphOrigin.Y),
            MapGlyph(geometry.GlyphOrigin.X + geometry.GlyphSize.X, geometry.GlyphOrigin.Y + geometry.GlyphSize.Y),
            MapGlyph(geometry.GlyphOrigin.X, geometry.GlyphOrigin.Y + geometry.GlyphSize.Y)
        };
        var points = glyph.Append(offset).Append(pivot).ToArray();
        var minimumX = Math.Min(0, points.Min(point => point.X));
        var minimumY = Math.Min(0, points.Min(point => point.Y));
        var maximumX = Math.Max(geometry.ParentSize.X, points.Max(point => point.X));
        var maximumY = Math.Max(geometry.ParentSize.Y, points.Max(point => point.Y));
        var visible = new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY);
        var viewport = new Rect(PADDING, PADDING, Bounds.Width - PADDING * 2,
            Bounds.Height - PADDING * 2 - LEGEND_HEIGHT);
        var scale = Math.Min(viewport.Width / visible.Width, viewport.Height / visible.Height);
        var origin = viewport.Center - new Vector(visible.Width * scale / 2, visible.Height * scale / 2);
        var canvas = new Rect(Map(new(0, 0)), Map(new(geometry.ParentSize.X, geometry.ParentSize.Y)));
        context.DrawRectangle(null, new Pen(Foreground, 1), canvas);

        var outline = new StreamGeometry();
        using (var path = outline.Open())
        {
            path.BeginFigure(Map(glyph[0]), true);
            foreach (var point in glyph.Skip(1))
            {
                path.LineTo(Map(point));
            }
            path.EndFigure(true);
        }
        var glyphPen = new Pen(Brushes.SeaGreen, 1.5,
            geometry.HasInk ? null : new DashStyle([3, 3], 0));
        context.DrawGeometry(new SolidColorBrush(Color.Parse("#1832CD32")), glyphPen, outline);
        var mappedAnchor = Map(anchor);
        var mappedOffset = Map(offset);
        context.DrawLine(new Pen(Foreground, 1), mappedAnchor, mappedOffset);
        var direction = mappedOffset - mappedAnchor;
        var distance = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (distance > 8)
        {
            var unit = direction / distance;
            var perpendicular = new Vector(-unit.Y, unit.X);
            context.DrawLine(new Pen(Foreground, 1), mappedOffset, mappedOffset - unit * 5 + perpendicular * 3);
            context.DrawLine(new Pen(Foreground, 1), mappedOffset, mappedOffset - unit * 5 - perpendicular * 3);
        }
        if (offset != pivot)
        {
            context.DrawLine(new Pen(Foreground, 1, new DashStyle([2, 2], 0)), mappedOffset, Map(pivot));
        }
        DrawAnchor(context, mappedAnchor);
        DrawPivot(context, Map(pivot));
        var legendY = Bounds.Height - LEGEND_HEIGHT + 3;
        DrawAnchor(context, new(9, legendY + 6));
        DrawText(context, "A", new(16, legendY), Brushes.RoyalBlue);
        DrawPivot(context, new(40, legendY + 6));
        DrawText(context, "P", new(48, legendY), Brushes.DarkOrange);
        var delta = string.Create(CultureInfo.CurrentCulture, $"Δ {position.Offset.X:0.##}, {position.Offset.Y:0.##} px");
        DrawText(context, delta, new(67, legendY), Foreground);

        Point MapGlyph(double x, double y)
        {
            var relativeX = (x - localPivot.X) * geometry.Transform.ScaleX;
            var relativeY = (y - localPivot.Y) * geometry.Transform.ScaleY;
            return pivot + new Vector(cosine * relativeX - sine * relativeY, sine * relativeX + cosine * relativeY);
        }

        Point Map(Point point)
        {
            return origin + new Vector((point.X - visible.Left) * scale, (point.Y - visible.Top) * scale);
        }
    }

    private static void DrawAnchor(DrawingContext context, Point point)
    {
        context.DrawEllipse(Brushes.RoyalBlue, null, point, 3, 3);
    }

    private static void DrawPivot(DrawingContext context, Point point)
    {
        var pen = new Pen(Brushes.DarkOrange, 1.5);
        context.DrawEllipse(null, pen, point, 4, 4);
        context.DrawLine(pen, point - new Vector(2, 0), point + new Vector(2, 0));
        context.DrawLine(pen, point - new Vector(0, 2), point + new Vector(0, 2));
    }

    private void DrawText(DrawingContext context, string text, Point point, IBrush? brush)
    {
        using var layout = WorkbenchTextFormatting.CreateLayout(this, text, 10, brush, LEGEND_HEIGHT);
        layout.Draw(context, point);
    }
}
