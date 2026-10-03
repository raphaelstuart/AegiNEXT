using System.Globalization;
using System.Collections.Immutable;
using System.Text;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>预览与压制共用的 CPU F16 工程渲染器；实例及缓存限同一线程使用。</summary>
public sealed class ProjectSceneRenderer : IDisposable
{
    private const float PREVIEW_REFERENCE_WHITE_NITS = 203;
    private readonly IProjectAssetResolver assets;
    private readonly Dictionary<ProjectAsset, SKImage> images = [];
    private readonly Dictionary<(string Text, SubtitleStyle Style, int Width), SubtitleLayout> layouts = [];
    private readonly SKColorSpace linear = SKColorSpace.CreateSrgbLinear();
    private PreparedProjectScene? prepared;
    private bool isDisposed;
    private LinearRenderSurface? previewScene;
    private LinearRenderSurface? previewTarget;
    private ImmutableArray<EvaluatedLayer> previewLayers = [];
    private bool previewSceneValid;
    private SKBlender? additiveBlend;
    private SKColorFilter? previewWhiteFilter;

    /// <summary>绑定字体、图片资源解析器，渲染器不接管解析器生命周期。</summary>
    public ProjectSceneRenderer(IProjectAssetResolver assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        this.assets = assets;
    }

    /// <summary>在工程精确时间渲染透明的预乘 F16 字幕与图形层，调用方负责释放结果。</summary>
    public LinearRenderSurface Render(ProjectDocument document, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(document);
        var surface = new LinearRenderSurface(new(document.Width, document.Height, (float)document.ReferenceWhiteNits));
        try
        {
            RenderInto(document, time, surface);
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <summary>清空并复用匹配工程尺寸和参考白的表面；效果与分组始终在线性光中合成。</summary>
    public void RenderInto(ProjectDocument document, MediaTime time, LinearRenderSurface destination)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Info.Width != document.Width || destination.Info.Height != document.Height ||
            !destination.Info.ReferenceWhiteNits.Equals((float)document.ReferenceWhiteNits))
        {
            throw new ArgumentException("渲染表面必须匹配工程尺寸和参考白。", nameof(destination));
        }

        Prepare(document);
        var layers = SceneEvaluator.Evaluate(prepared!, time);
        destination.Clear();
        foreach (var layer in layers)
        {
            DrawLayer(document, destination.Canvas, layer);
        }
    }

    /// <summary>将工程叠层重标到 203 nit 名义白，与 sRGB BGRA 背景在线性光合成；只缩放叠层 RGB，不改变 alpha 或导出表面。</summary>
    public byte[] ComposePreview(ProjectDocument document, MediaTime time, ReadOnlySpan<byte> bgra, int width, int height, int rowBytes)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (width <= 0 || height <= 0 || rowBytes < checked(width * 4) || bgra.Length < checked(rowBytes * height))
        {
            throw new ArgumentException("无效的预览缓冲。", nameof(bgra));
        }

        Prepare(document);
        previewScene ??= new(new(document.Width, document.Height, (float)document.ReferenceWhiteNits));
        if (previewTarget is null || previewTarget.Info.Width != width || previewTarget.Info.Height != height)
        {
            previewTarget?.Dispose();
            previewTarget = new(new(width, height, PREVIEW_REFERENCE_WHITE_NITS));
        }

        var layers = SceneEvaluator.Evaluate(prepared!, time);
        if (!previewSceneValid || !Equivalent(previewLayers, layers))
        {
            previewScene.Clear();
            foreach (var evaluated in layers)
            {
                DrawLayer(document, previewScene.Canvas, evaluated);
            }

            previewLayers = layers;
            previewSceneValid = true;
        }

        var scene = previewScene;
        var result = previewTarget;
        using var srgb = SKColorSpace.CreateSrgb();
        using var background = SKImage.FromPixelCopy(new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb), bgra, rowBytes)
            ?? throw new InvalidOperationException("无法读取视频预览。");
        result.Canvas.DrawImage(background, 0, 0);
        using var layer = scene.Snapshot();
        if (document.ReferenceWhiteNits != PREVIEW_REFERENCE_WHITE_NITS)
        {
            previewWhiteFilter ??= CreatePreviewWhiteFilter((float)(document.ReferenceWhiteNits / PREVIEW_REFERENCE_WHITE_NITS));
        }

        using var layerPaint = new SKPaint { ColorFilter = previewWhiteFilter };
        result.Canvas.DrawImage(layer, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear), layerPaint);
        return result.CopySrgbBgra();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        foreach (var image in images.Values)
        {
            image.Dispose();
        }

        ClearLayouts();
        ClearPreview();
        images.Clear();
        additiveBlend?.Dispose();
        linear.Dispose();
        isDisposed = true;
    }

    private void Prepare(ProjectDocument document)
    {
        if (!ReferenceEquals(prepared?.Document, document))
        {
            prepared = new(document);
            ClearPreview();
            ClearLayouts();
            foreach (var image in images.Values)
            {
                image.Dispose();
            }

            images.Clear();
        }

    }

    private void ClearPreview()
    {
        previewScene?.Dispose();
        previewTarget?.Dispose();
        previewWhiteFilter?.Dispose();
        previewWhiteFilter = null;
        previewScene = null;
        previewTarget = null;
        previewLayers = [];
        previewSceneValid = false;
    }

    private static bool Equivalent(ImmutableArray<EvaluatedLayer> previous, ImmutableArray<EvaluatedLayer> current)
    {
        if (previous.Length != current.Length)
        {
            return false;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            var a = previous[i];
            var b = current[i];
            if (!ReferenceEquals(a.Source, b.Source) || a.Transform != b.Transform || a.Opacity != b.Opacity ||
                a.Fill != b.Fill || a.Stroke != b.Stroke || a.StrokeWidth != b.StrokeWidth || a.Blur != b.Blur ||
                (a.Subtitle is { Karaoke.IsEmpty: false } && a.LocalTime != b.LocalTime) || !Equivalent(a.Children, b.Children))
            {
                return false;
            }
        }

        return true;
    }

    private void DrawLayer(ProjectDocument document, SKCanvas parent, EvaluatedLayer layer)
    {
        using var surface = new LinearRenderSurface(new(document.Width, document.Height, (float)document.ReferenceWhiteNits));
        var canvas = surface.Canvas;
        canvas.SetMatrix(parent.TotalMatrix);
        var saved = canvas.Save();
        try
        {
            var transform = layer.Transform;
            canvas.Translate((float)transform.X, (float)transform.Y);
            canvas.RotateDegrees((float)transform.Rotation);
            canvas.Scale((float)transform.ScaleX, (float)transform.ScaleY);
            canvas.Translate((float)-transform.AnchorX, (float)-transform.AnchorY);
            if (layer.Source.Mask is { } mask)
            {
                using var path = Path(mask.Path);
                canvas.ClipPath(path, mask.Inverted ? SKClipOperation.Difference : SKClipOperation.Intersect, true);
            }

            using var blend = Paint(new(1, 1, 1, layer.Opacity));
            blend.BlendMode = layer.Source.Blend switch
            {
                BlendMode.NORMAL => SKBlendMode.SrcOver,
                BlendMode.MULTIPLY => SKBlendMode.Multiply,
                BlendMode.SCREEN => SKBlendMode.Screen,
                BlendMode.ADD => SKBlendMode.Plus,
                BlendMode.OVERLAY => SKBlendMode.Overlay,
                BlendMode.DARKEN => SKBlendMode.Darken,
                BlendMode.LIGHTEN => SKBlendMode.Lighten,
                BlendMode.DIFFERENCE => SKBlendMode.Difference,
                _ => throw new InvalidDataException("未知图层混合方式。")
            };
            if (layer.Source.Blend == BlendMode.ADD)
            {
                additiveBlend ??= CreateAdditiveBlend();
                blend.Blender = additiveBlend;
            }

            switch (layer.Source.Kind)
            {
                case LayerKind.SUBTITLE:
                    DrawSubtitle(document, canvas, layer);
                    break;
                case LayerKind.SHAPE:
                    DrawShape(canvas, layer);
                    break;
                case LayerKind.IMAGE:
                    DrawImage(document, canvas, layer.Source.Image!);
                    break;
                case LayerKind.GROUP:
                    foreach (var child in layer.Children)
                    {
                        DrawLayer(document, canvas, child);
                    }

                    break;
                default:
                    throw new InvalidDataException("未知图层类型。");
            }

            F16LayerBlur.Apply(surface, layer.Blur);
            using var snapshot = surface.Snapshot();
            var parentSave = parent.Save();
            try
            {
                parent.ResetMatrix();
                parent.DrawImage(snapshot, 0, 0, blend);
            }
            finally
            {
                parent.RestoreToCount(parentSave);
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }

    private void DrawShape(SKCanvas canvas, EvaluatedLayer layer)
    {
        var shape = layer.Source.Shape!;
        using var path = shape.Kind == ShapeKind.PATH ? Path(shape.Path!) : new SKPath();
        if (shape.Kind == ShapeKind.RECTANGLE)
        {
            path.AddRect(new(0, 0, (float)shape.Width, (float)shape.Height));
        }
        else if (shape.Kind == ShapeKind.ELLIPSE)
        {
            path.AddOval(new(0, 0, (float)shape.Width, (float)shape.Height));
        }

        using var fill = Paint(layer.Fill);
        canvas.DrawPath(path, fill);
        if (layer.StrokeWidth > 0)
        {
            using var stroke = Paint(layer.Stroke);
            stroke.Style = SKPaintStyle.Stroke;
            stroke.StrokeWidth = (float)layer.StrokeWidth;
            stroke.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawPath(path, stroke);
        }
    }

    private void DrawImage(ProjectDocument document, SKCanvas canvas, LayerImage layer)
    {
        var asset = document.Assets.Single(value => value.Id == layer.AssetId);
        if (!images.TryGetValue(asset, out var image))
        {
            using var stream = assets.Open(asset);
            using var data = SKData.Create(stream);
            image = SKImage.FromEncodedData(data) ?? throw new InvalidDataException("无法解码工程图片。");
            if ((long)image.Width * image.Height > 33177600)
            {
                image.Dispose();
                throw new InvalidDataException("图片超过像素预算。");
            }

            images.Add(asset, image);
        }

        var width = (float)layer.Width;
        var height = (float)layer.Height;
        var bounds = new SKRect(0, 0, width, height);
        canvas.ClipRect(bounds, SKClipOperation.Intersect, true);
        if (layer.Fit != ImageFit.STRETCH)
        {
            var scale = layer.Fit == ImageFit.FIT ? Math.Min(width / image.Width, height / image.Height) : Math.Max(width / image.Width, height / image.Height);
            var targetWidth = image.Width * scale;
            var targetHeight = image.Height * scale;
            bounds = new((width - targetWidth) / 2, (height - targetHeight) / 2, (width + targetWidth) / 2, (height + targetHeight) / 2);
        }

        canvas.DrawImage(image, bounds, new SKSamplingOptions(SKFilterMode.Linear));
    }

    private void DrawSubtitle(ProjectDocument document, SKCanvas canvas, EvaluatedLayer layer)
    {
        var subtitle = layer.Subtitle!;
        var style = subtitle.Style;
        var layout = Layout(document, subtitle);
        var lineHeight = (float)(style.FontSize * style.LineHeight);
        var blockHeight = (float)style.FontSize + Math.Max(0, layout.Lines.Count - 1) * lineHeight;
        var vertical = (int)style.Alignment / 3;
        var top = vertical == 0 ? (float)style.Margin : vertical == 1 ? (document.Height - blockHeight) / 2 : document.Height - (float)style.Margin - blockHeight;
        for (var index = 0; index < layout.Lines.Count; index++)
        {
            var line = layout.Lines[index];
            if (line.Run is not { } run)
            {
                continue;
            }

            var horizontal = (int)style.Alignment % 3;
            var x = horizontal == 0 ? (float)style.Margin : horizontal == 1 ? (document.Width - run.AdvanceWidth) / 2 : document.Width - (float)style.Margin - run.AdvanceWidth;
            var y = top + (float)style.FontSize + index * lineHeight;
            if (style.ShadowColor.Alpha > 0)
            {
                using var shadow = Paint(style.ShadowColor);
                using var filter = style.ShadowBlur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)style.ShadowBlur) : null;
                shadow.MaskFilter = filter;
                canvas.DrawText(run.GetBlob(), x + (float)style.ShadowOffset.X, y + (float)style.ShadowOffset.Y, shadow);
            }

            if (layer.StrokeWidth > 0)
            {
                using var stroke = Paint(layer.Stroke);
                stroke.Style = SKPaintStyle.Stroke;
                stroke.StrokeWidth = (float)layer.StrokeWidth * 2;
                stroke.StrokeJoin = SKStrokeJoin.Round;
                canvas.DrawText(run.GetBlob(), x, y, stroke);
            }

            using var fill = Paint(layer.Fill);
            canvas.DrawText(run.GetBlob(), x, y, fill);
            foreach (var karaoke in subtitle.Karaoke)
            {
                if (layer.LocalTime <= karaoke.Start)
                {
                    continue;
                }

                var start = Math.Max(0, karaoke.Utf16Start - line.Utf16Offset);
                var end = Math.Min(line.Text.Length, karaoke.Utf16Start + karaoke.Utf16Length - line.Utf16Offset);
                if (end <= start)
                {
                    continue;
                }

                var min = float.MaxValue;
                var max = float.MinValue;
                foreach (var glyph in run.Glyphs)
                {
                    if (glyph.Utf16Cluster >= start && glyph.Utf16Cluster < end)
                    {
                        min = Math.Min(min, glyph.Position.X);
                        max = Math.Max(max, glyph.Position.X);
                    }
                }

                if (min == float.MaxValue)
                {
                    continue;
                }

                var right = run.AdvanceWidth;
                foreach (var glyph in run.Glyphs)
                {
                    if (glyph.Position.X > max)
                    {
                        right = Math.Min(right, glyph.Position.X);
                    }
                }

                var elapsed = layer.LocalTime - karaoke.Start;
                var duration = karaoke.End - karaoke.Start;
                var progress = Math.Clamp(((double)elapsed.Numerator / elapsed.Denominator) / ((double)duration.Numerator / duration.Denominator), 0, 1);
                var save = canvas.Save();
                canvas.ClipRect(new(x + min, y - (float)style.FontSize * 1.5f, x + min + (right - min) * (float)progress, y + (float)style.FontSize), SKClipOperation.Intersect, true);
                using var highlight = Paint(karaoke.HighlightColor);
                canvas.DrawText(run.GetBlob(), x, y, highlight);
                canvas.RestoreToCount(save);
            }
        }
    }

    private SubtitleLayout Layout(ProjectDocument document, SubtitleLine subtitle)
    {
        var key = (subtitle.Text, subtitle.Style, document.Width);
        if (layouts.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (layouts.Count >= 256)
        {
            ClearLayouts();
        }

        var lines = new List<SubtitleLayoutLine>();
        var offset = 0;
        try
        {
            foreach (var paragraph in subtitle.Text.Split('\n'))
            {
                var text = paragraph.TrimEnd('\r');
                if (text.Length == 0)
                {
                    lines.Add(new(string.Empty, offset, null));
                    offset += paragraph.Length + 1;
                    continue;
                }

                using var shaper = new TextShaper(Typeface(document, subtitle.Style, text));
                var direction = text.EnumerateRunes().Any(value => value.Value is >= 0x0590 and <= 0x08ff) ? TextDirection.RIGHT_TO_LEFT : TextDirection.LEFT_TO_RIGHT;
                var boundaries = StringInfo.ParseCombiningCharacters(text);
                var begin = 0;
                while (begin < text.Length)
                {
                    var end = text.Length;
                    var run = shaper.Shape(text[begin..end], (float)subtitle.Style.FontSize, direction, "und");
                    var available = Math.Max(1, document.Width - subtitle.Style.Margin * 2);
                    if (run.AdvanceWidth > available)
                    {
                        run.Dispose();
                        var startBoundary = Array.BinarySearch(boundaries, begin);
                        var low = startBoundary + 1;
                        var high = boundaries.Length;
                        while (low < high)
                        {
                            var middle = (low + high + 1) / 2;
                            var candidateEnd = middle == boundaries.Length ? text.Length : boundaries[middle];
                            using var candidate = shaper.Shape(text[begin..candidateEnd], (float)subtitle.Style.FontSize, direction, "und");
                            if (candidate.AdvanceWidth <= available)
                            {
                                low = middle;
                            }
                            else
                            {
                                high = middle - 1;
                            }
                        }

                        end = low == boundaries.Length ? text.Length : boundaries[low];
                        run = shaper.Shape(text[begin..end], (float)subtitle.Style.FontSize, direction, "und");
                    }

                    lines.Add(new(text[begin..end], offset + begin, run));
                    begin = end;
                }

                offset += paragraph.Length + 1;
            }

            var result = new SubtitleLayout(lines);
            layouts.Add(key, result);
            return result;
        }
        catch
        {
            foreach (var line in lines)
            {
                line.Run?.Dispose();
            }

            throw;
        }
    }

    private SKTypeface Typeface(ProjectDocument document, SubtitleStyle style, string text)
    {
        if (style.FontAssetId is { } id)
        {
            using var stream = assets.Open(document.Assets.Single(value => value.Id == id));
            using var data = SKData.Create(stream);
            return SKTypeface.FromData(data) ?? throw new InvalidDataException("无法打开工程字体。");
        }

        var fontStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var requested = SKTypeface.FromFamilyName(style.FontFamily, fontStyle);
        using (var font = new SKFont(requested))
        {
            if (font.ContainsGlyphs(text))
            {
                return requested;
            }
        }

        requested.Dispose();
        foreach (var rune in text.EnumerateRunes())
        {
            var fallback = SKFontManager.Default.MatchCharacter(style.FontFamily, fontStyle, ["und"], rune.Value);
            if (fallback is null)
            {
                continue;
            }

            using var font = new SKFont(fallback);
            if (font.ContainsGlyphs(text))
            {
                return fallback;
            }

            fallback.Dispose();
        }

        throw new InvalidDataException("没有覆盖该段文字的字体，请导入工程字体资源。");
    }

    private static SKColorFilter CreatePreviewWhiteFilter(float scale)
    {
        using var effect = SKRuntimeEffect.CreateColorFilter("uniform float scale; half4 main(half4 c) { return half4(c.rgb * scale, c.a); }", out var error)
            ?? throw new InvalidOperationException($"无法创建预览参考白转换：{error}");
        using var uniforms = new SKRuntimeEffectUniforms(effect) { ["scale"] = scale };
        return effect.ToColorFilter(uniforms) ?? throw new InvalidOperationException("无法创建预览参考白转换实例。");
    }

    private static SKBlender CreateAdditiveBlend()
    {
        using var effect = SKRuntimeEffect.CreateBlender("half4 main(half4 s, half4 d) { return half4(clamp(s.rgb + d.rgb, -65504.0, 65504.0), min(s.a + d.a, 1.0)); }", out var error)
            ?? throw new InvalidOperationException($"无法创建线性 HDR 加法混合：{error}");
        return effect.ToBlender() ?? throw new InvalidOperationException("无法创建线性 HDR 混合实例。");
    }

    private SKPaint Paint(SceneColor color)
    {
        var paint = new SKPaint { IsAntialias = true };
        paint.SetColor(new((float)color.Red, (float)color.Green, (float)color.Blue, (float)color.Alpha), linear);
        return paint;
    }

    private static SKPath Path(PathGeometry geometry)
    {
        var path = new SKPath();
        path.MoveTo((float)geometry.Start.X, (float)geometry.Start.Y);
        foreach (var segment in geometry.Segments)
        {
            path.CubicTo((float)segment.Control1.X, (float)segment.Control1.Y, (float)segment.Control2.X,
                (float)segment.Control2.Y, (float)segment.End.X, (float)segment.End.Y);
        }

        if (geometry.Closed)
        {
            path.Close();
        }

        return path;
    }

    private void ClearLayouts()
    {
        foreach (var layout in layouts.Values)
        {
            layout.Dispose();
        }

        layouts.Clear();
    }
}
