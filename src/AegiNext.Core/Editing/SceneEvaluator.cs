using System.Collections.Immutable;
using System.Collections.Frozen;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>纯函数动画求值，不依赖时钟、文件系统或渲染框架。</summary>
public static class SceneEvaluator
{
    /// <summary>验证工程并按精确半开时间区间求出可见合成树。</summary>
    public static ImmutableArray<EvaluatedLayer> Evaluate(ProjectDocument document, MediaTime time)
    {
        return Evaluate(new PreparedProjectScene(document), time);
    }

    /// <summary>求值已验证快照，适用于预览及逐帧压制。</summary>
    public static ImmutableArray<EvaluatedLayer> Evaluate(PreparedProjectScene scene, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return EvaluateLayers(scene.Document.Layers, scene.Subtitles, time);
    }

    /// <summary>求严格递增轨道的保持、线性或缓动值；时间在首尾之外时保持端点。</summary>
    public static AnimationValue EvaluateTrack(AnimationTrack track, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.Keyframes.IsDefaultOrEmpty)
        {
            throw new ArgumentException("轨道没有关键帧。", nameof(track));
        }

        var frames = track.Keyframes;
        if (time <= frames[0].Time)
        {
            return frames[0].Value;
        }

        if (time >= frames[^1].Time)
        {
            return frames[^1].Value;
        }

        var lower = 1;
        var upper = frames.Length - 1;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (time >= frames[middle].Time)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        var first = frames[lower - 1];
        var second = frames[lower];
        var fraction = Fraction(time - first.Time, second.Time - first.Time);
        var firstFraction = CurveFraction(first.Interpolation, first.CurveStart, first.CurveEnd, fraction);
        var value = AnimationValue.Lerp(first.Value, second.Value, firstFraction);
        for (var component = 1; component < first.Value.ComponentCount; component++)
        {
            if (!first.ComponentCurves.IsDefaultOrEmpty && first.ComponentCurves[component - 1] is { } curve)
            {
                var componentFraction = CurveFraction(curve.Interpolation, curve.CurveStart, curve.CurveEnd, fraction);
                var start = first.Value.GetComponent(component);
                value = value.WithComponent(component, start + (second.Value.GetComponent(component) - start) * componentFraction);
            }
        }

        return value;
    }

    /// <summary>读取标量轨道值，拒绝向量轨道。</summary>
    public static double EvaluateScalarTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Scalar;

    /// <summary>读取二维向量轨道值，拒绝标量轨道。</summary>
    public static ScenePoint EvaluateVectorTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Vector;

    /// <summary>读取完整线性 RGBA 轨道值，拒绝其他维度。</summary>
    public static SceneColor EvaluateColorTrack(AnimationTrack track, MediaTime time) => EvaluateTrack(track, time).Color;

    private static double CurveFraction(KeyframeInterpolation interpolation, double start, double end, double fraction)
    {
        var range = end - start;
        var offset = range * fraction;
        return interpolation switch
        {
            KeyframeInterpolation.HOLD => 0,
            KeyframeInterpolation.LINEAR => fraction,
            KeyframeInterpolation.EASE_IN => fraction * (2 * start + offset) / (2 * start + range),
            KeyframeInterpolation.EASE_OUT => fraction * (2 * (1 - end) + range * (2 - fraction)) /
                (2 * (1 - end) + range),
            KeyframeInterpolation.EASE_IN_OUT => fraction *
                (6 * start * (1 - start) + 3 * offset * (1 - 2 * start) - 2 * offset * offset) /
                (6 * start * (1 - start) + 3 * range * (1 - 2 * start) - 2 * range * range),
            _ => throw new InvalidDataException("未知关键帧插值。")
        };
    }

    /// <summary>以 [0,1] 段参数求路径位置，超界参数夹在首尾。</summary>
    public static ScenePoint EvaluatePath(PathGeometry path, double progress)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Segments.IsDefaultOrEmpty || !double.IsFinite(progress))
        {
            throw new ArgumentException("路径或参数无效。", nameof(path));
        }

        var scaled = Math.Clamp(progress, 0, 1) * path.Segments.Length;
        var index = Math.Min((int)scaled, path.Segments.Length - 1);
        var t = scaled - index;
        var a = index == 0 ? path.Start : path.Segments[index - 1].End;
        var segment = path.Segments[index];
        var inverse = 1 - t;
        return new(inverse * inverse * inverse * a.X + 3 * inverse * inverse * t * segment.Control1.X +
            3 * inverse * t * t * segment.Control2.X + t * t * t * segment.End.X,
            inverse * inverse * inverse * a.Y + 3 * inverse * inverse * t * segment.Control1.Y +
            3 * inverse * t * t * segment.Control2.Y + t * t * t * segment.End.Y);
    }

    private static ImmutableArray<EvaluatedLayer> EvaluateLayers(ImmutableArray<ProjectLayer> layers,
        FrozenDictionary<Guid, SubtitleLine> subtitles, MediaTime time)
    {
        var result = ImmutableArray.CreateBuilder<EvaluatedLayer>();
        foreach (var layer in layers)
        {
            if (time < layer.Start || time >= layer.End)
            {
                continue;
            }

            var subtitle = layer.SubtitleId is { } id ? subtitles[id] : null;
            var local = time - layer.Start + layer.AnimationOffset;
            var values = layer.Tracks.ToDictionary(track => track.Property, track => EvaluateTrack(track, local));
            var transform = layer.Transform with
            {
                Position = GetVector(values, AnimationProperty.POSITION, layer.Transform.Position),
                Scale = GetVector(values, AnimationProperty.SCALE, layer.Transform.Scale),
                Rotation = Get(values, AnimationProperty.ROTATION, layer.Transform.Rotation)
            };
            if (layer.MotionPath is { } motion)
            {
                var progress = Get(values, AnimationProperty.PATH_PROGRESS, Fraction(local, motion.Duration));
                var point = EvaluatePath(motion.Path, progress);
                var rotation = transform.Rotation;
                if (motion.OrientToPath)
                {
                    var before = EvaluatePath(motion.Path, progress - 0.00001);
                    var after = EvaluatePath(motion.Path, progress + 0.00001);
                    rotation += Math.Atan2(after.Y - before.Y, after.X - before.X) * 180 / Math.PI;
                }

                transform = transform with { X = transform.X + point.X, Y = transform.Y + point.Y, Rotation = rotation };
            }

            var fill = subtitle?.Style.Fill ?? layer.Fill;
            var stroke = subtitle?.Style.Stroke ?? layer.Stroke;
            result.Add(new(layer, local, transform, Get(values, AnimationProperty.OPACITY, layer.Opacity),
                GetColor(values, AnimationProperty.FILL, fill), GetColor(values, AnimationProperty.STROKE, stroke),
                Get(values, AnimationProperty.STROKE_WIDTH, subtitle?.Style.StrokeWidth ?? layer.StrokeWidth),
                Get(values, AnimationProperty.BLUR, layer.Blur), subtitle, EvaluateLayers(layer.Children, subtitles, time)));
        }

        return result.ToImmutable();
    }

    private static double Get(Dictionary<AnimationProperty, AnimationValue> values, AnimationProperty property, double fallback)
    {
        return values.TryGetValue(property, out var value) ? value.Scalar : fallback;
    }

    private static ScenePoint GetVector(Dictionary<AnimationProperty, AnimationValue> values, AnimationProperty property, ScenePoint fallback)
    {
        return values.TryGetValue(property, out var value) ? value.Vector : fallback;
    }

    private static SceneColor GetColor(Dictionary<AnimationProperty, AnimationValue> values, AnimationProperty property, SceneColor fallback)
    {
        return values.TryGetValue(property, out var value) ? value.Color : fallback;
    }

    private static double Fraction(MediaTime elapsed, MediaTime duration)
    {
        return Math.Clamp(((double)elapsed.Numerator / elapsed.Denominator) /
            ((double)duration.Numerator / duration.Denominator), 0, 1);
    }
}
