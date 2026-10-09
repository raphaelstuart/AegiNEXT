using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    /// <summary>在指定字幕局部矩形内绘制透明编辑预览；保持工程排版，可固定普通或高亮外观，不应用图层动画与变换，调用方释放表面。</summary>
    public LinearRenderSurface RenderSubtitlePreview(ProjectDocument document, SubtitleLine subtitle,
        MediaTime localTime, SKRect cropBounds, SubtitlePreviewMode previewMode = SubtitlePreviewMode.TIMED)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subtitle);
        if (!Enum.IsDefined(previewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(previewMode));
        }
        if (!float.IsFinite(cropBounds.Left) || !float.IsFinite(cropBounds.Top) ||
            !float.IsFinite(cropBounds.Right) || !float.IsFinite(cropBounds.Bottom) ||
            cropBounds.Width is <= 0 or > 32768 || cropBounds.Height is <= 0 or > 32768 ||
            Math.Ceiling(cropBounds.Width) * Math.Ceiling(cropBounds.Height) > 33177600)
        {
            throw new ArgumentOutOfRangeException(nameof(cropBounds));
        }
        Prepare(document);
        var surface = new LinearRenderSurface(new((int)Math.Ceiling(cropBounds.Width),
            (int)Math.Ceiling(cropBounds.Height), (float)document.ReferenceWhiteNits), graphicsContext);
        try
        {
            surface.Canvas.Translate(-cropBounds.Left, -cropBounds.Top);
            var source = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id };
            var layer = new EvaluatedLayer(source, localTime, new(), 1, subtitle.Style.Fill, subtitle.Style.Stroke,
                subtitle.Style.StrokeWidth, 0, subtitle);
            DrawSubtitle(document, surface.Canvas, layer, previewMode);
            surface.Canvas.ResetMatrix();
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private void DrawSubtitle(ProjectDocument document, SKCanvas canvas, EvaluatedLayer layer,
        SubtitlePreviewMode previewMode = SubtitlePreviewMode.TIMED)
    {
        var subtitle = layer.Subtitle!;
        foreach (var line in Layout(document, subtitle).Lines)
        {
            foreach (var run in line.Runs)
            {
                var normal = AnimatedStyle(run.Style, layer);
                if (run.Karaoke.IsEmpty || previewMode == SubtitlePreviewMode.NORMAL)
                {
                    DrawSubtitleRun(canvas, run, normal);
                    continue;
                }
                DrawKaraokeRun(canvas, run, subtitle, layer, normal, previewMode);

            }
        }
    }

    private void DrawKaraokeRun(SKCanvas canvas, SubtitleLayoutRun run, SubtitleLine subtitle,
        EvaluatedLayer layer, SubtitleStyle normal, SubtitlePreviewMode previewMode)
    {
        using var excluded = new SKPath();
        using var activeRegions = new SKPath();
        var inactiveStyles = new Dictionary<SubtitleStyle, SKPath>();
        var activeStyles = new Dictionary<SubtitleStyle, SKPath>();
        var overlays = new Dictionary<SceneColor, SKPath>();
        try
        {
            foreach (var span in run.Karaoke)
            {
                var segment = span.Segment;
                var replacement = previewMode == SubtitlePreviewMode.HIGHLIGHTED ||
                    subtitle.KaraokeStyle is not null || segment.ActiveStyle is not null ||
                    segment.InactiveStyle is not null || segment.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP;
                if (!replacement)
                {
                    if (ActiveKaraokeBounds(run, span, layer.LocalTime, 0) is { } overlay)
                    {
                        AddRegion(overlays, segment.HighlightColor, overlay);
                    }
                    continue;
                }
                var inactive = KaraokeVisualStyleResolver.ResolveInactive(normal, segment);
                var active = KaraokeVisualStyleResolver.ResolveActive(normal, subtitle.KaraokeStyle, segment);
                if (inactive == normal && active == normal)
                {
                    continue;
                }
                var padding = Padding(normal, inactive, active);
                var highlight = previewMode == SubtitlePreviewMode.HIGHLIGHTED
                    ? FullKaraokeBounds(run, span, padding)
                    : ActiveKaraokeBounds(run, span, layer.LocalTime, padding);
                if (inactive != normal)
                {
                    var full = FullKaraokeBounds(run, span, padding);
                    excluded.AddRect(full);
                    AddRegion(inactiveStyles, inactive, full);
                }
                else if (highlight is { } replaced)
                {
                    excluded.AddRect(replaced);
                }
                if (highlight is { } bounds)
                {
                    activeRegions.AddRect(bounds);
                    AddRegion(activeStyles, active, bounds);
                }
            }
            var save = canvas.Save();
            canvas.ClipPath(excluded, SKClipOperation.Difference, true);
            DrawSubtitleRun(canvas, run, normal);
            canvas.RestoreToCount(save);
            foreach (var pair in inactiveStyles)
            {
                save = canvas.Save();
                canvas.ClipPath(pair.Value, SKClipOperation.Intersect, true);
                canvas.ClipPath(activeRegions, SKClipOperation.Difference, true);
                DrawSubtitleRun(canvas, run, pair.Key);
                canvas.RestoreToCount(save);
            }
            foreach (var pair in activeStyles)
            {
                save = canvas.Save();
                canvas.ClipPath(pair.Value, SKClipOperation.Intersect, true);
                DrawSubtitleRun(canvas, run, pair.Key);
                canvas.RestoreToCount(save);
            }
            foreach (var pair in overlays)
            {
                save = canvas.Save();
                canvas.ClipPath(pair.Value, SKClipOperation.Intersect, true);
                using var paint = Paint(pair.Key);
                DrawRunInk(canvas, run, paint, SKPoint.Empty);
                canvas.RestoreToCount(save);
            }
        }
        finally
        {
            foreach (var path in inactiveStyles.Values.Concat(activeStyles.Values).Concat(overlays.Values))
            {
                path.Dispose();
            }
        }
    }

    private static void AddRegion<T>(Dictionary<T, SKPath> regions, T key, SKRect bounds) where T : notnull
    {
        if (!regions.TryGetValue(key, out var path))
        {
            path = new();
            regions.Add(key, path);
        }
        path.AddRect(bounds);
    }

    private static SubtitleStyle AnimatedStyle(SubtitleStyle style, EvaluatedLayer layer)
    {
        return style with
        {
            Fill = layer.HasFillAnimation ? layer.Fill : style.Fill,
            Stroke = layer.HasStrokeAnimation ? layer.Stroke : style.Stroke,
            StrokeWidth = layer.HasStrokeWidthAnimation ? layer.StrokeWidth : style.StrokeWidth
        };
    }

    private static float Padding(params SubtitleStyle[] styles)
    {
        return (float)styles.Max(style => style.StrokeWidth +
            Math.Max(Math.Abs(style.ShadowOffset.X), Math.Abs(style.ShadowOffset.Y)) + style.ShadowBlur * 4 + 1);
    }

    private static SKRect FullKaraokeBounds(SubtitleLayoutRun run, SubtitleKaraokeSpan span, float padding)
    {
        var bounds = span.Bounds;
        var ink = RunInkBounds(run);
        ink.Offset(run.Position);
        if (padding > 0 && (!span.RightToLeft && span.StartsRun || span.RightToLeft && span.EndsRun))
        {
            bounds.Left = Math.Min(bounds.Left, ink.Left) - padding;
        }
        if (padding > 0 && (!span.RightToLeft && span.EndsRun || span.RightToLeft && span.StartsRun))
        {
            bounds.Right = Math.Max(bounds.Right, ink.Right) + padding;
        }
        bounds.Top = run.Position.Y - (float)run.Style.FontSize * 1.5f - padding;
        bounds.Bottom = run.Position.Y + (float)run.Style.FontSize + padding;
        return bounds;
    }

    private static SKRect? ActiveKaraokeBounds(SubtitleLayoutRun run, SubtitleKaraokeSpan span, MediaTime time, float padding)
    {
        var segment = span.Segment;
        if (time < segment.Start || segment.HighlightKind == KaraokeHighlightKind.SWEEP && time == segment.Start)
        {
            return null;
        }
        var full = FullKaraokeBounds(run, span, padding);
        if (segment.HighlightKind != KaraokeHighlightKind.SWEEP || time >= segment.End)
        {
            return full;
        }
        var elapsed = time - segment.Start;
        var duration = segment.End - segment.Start;
        var progress = Math.Clamp(((double)elapsed.Numerator / elapsed.Denominator) /
            ((double)duration.Numerator / duration.Denominator), 0, 1);
        var advance = span.TotalAdvance * (float)progress - span.AdvanceBefore;
        if (advance <= 0)
        {
            return null;
        }
        if (advance >= span.Bounds.Width)
        {
            return full;
        }
        if (span.RightToLeft)
        {
            full.Left = span.Bounds.Right - advance;
        }
        else
        {
            full.Right = span.Bounds.Left + advance;
        }
        return full;
    }

    private void DrawSubtitleRun(SKCanvas canvas, SubtitleLayoutRun run, SubtitleStyle style)
    {
        if (style.ShadowColor.Alpha > 0)
        {
            using var shadow = Paint(style.ShadowColor);
            using var filter = style.ShadowBlur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)style.ShadowBlur) : null;
            shadow.MaskFilter = filter;
            DrawRunInk(canvas, run, shadow, new((float)style.ShadowOffset.X, (float)style.ShadowOffset.Y));
        }
        if (style.StrokeWidth > 0)
        {
            using var stroke = Paint(style.Stroke);
            stroke.Style = SKPaintStyle.Stroke;
            stroke.StrokeWidth = (float)style.StrokeWidth * 2;
            stroke.StrokeJoin = SKStrokeJoin.Round;
            DrawRunInk(canvas, run, stroke, SKPoint.Empty);
        }
        using var fill = Paint(style.Fill);
        DrawRunInk(canvas, run, fill, SKPoint.Empty);
    }

    private static void DrawRunInk(SKCanvas canvas, SubtitleLayoutRun run, SKPaint paint, SKPoint offset)
    {
        var position = new SKPoint(run.Position.X + offset.X, run.Position.Y + offset.Y);
        canvas.DrawText(run.Shape.GetBlob(), position.X, position.Y, paint);
        foreach (var decoration in Decorations(run))
        {
            var bounds = decoration;
            bounds.Offset(position);
            canvas.DrawRect(bounds, paint);
        }
    }
}
