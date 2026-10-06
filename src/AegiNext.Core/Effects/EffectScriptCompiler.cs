using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>按目标片段精确分配固定和自由时间，生成可持久化的既有动画轨道。</summary>
public static class EffectScriptCompiler
{
    /// <summary>以目标内容时钟编译脚本；使用应用前基础值，共享端点冲突或越界值整体失败。</summary>
    public static ImmutableArray<AnimationTrack> Compile(EffectScript script, ProjectLayer target, SubtitleStyle? subtitleStyle = null)
    {
        EffectScriptValidator.Validate(script);
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            return CompileCore(script, target, subtitleStyle);
        }
        catch (OverflowException error)
        {
            throw new EffectScriptException("脚本的精确时间超出有理数表示范围，请简化时长、权重或段内位置。", innerException: error);
        }
    }

    private static ImmutableArray<AnimationTrack> CompileCore(EffectScript script, ProjectLayer target, SubtitleStyle? subtitleStyle)
    {
        if (target.SubtitleId.HasValue && subtitleStyle is null &&
            script.Segments.Any(segment => segment.Keyframes.Any(frame => frame.Property is EffectScriptProperty.STROKE_WIDTH or EffectScriptProperty.FILL or EffectScriptProperty.STROKE)))
        {
            throw new EffectScriptException("字幕颜色和描边属性需要应用前的字幕样式。");
        }
        var (origin, end) = LayerAnimationTiming.GetRange(target);
        var duration = end - origin;
        if (duration <= MediaTime.Zero)
        {
            throw new EffectScriptException("片段中没有可应用特效的时间。");
        }

        var fixedTotal = script.Segments.Aggregate(MediaTime.Zero, (total, segment) => total + (segment.FixedDuration ?? MediaTime.Zero));
        var flexTotal = script.Segments.Sum(segment => segment.FlexWeight);
        var compress = duration < fixedTotal;
        if (compress && script.ShortClipPolicy == EffectScriptShortClipPolicy.REJECT)
        {
            throw new EffectScriptException($"片段时长 {duration} 小于固定段总时长 {fixedTotal}，该脚本拒绝应用。");
        }

        var remaining = compress ? MediaTime.Zero : duration - fixedTotal;
        var tracks = new Dictionary<AnimationProperty, List<Keyframe>>();
        var cursor = origin;
        foreach (var segment in script.Segments)
        {
            var length = segment.FixedDuration is { } fixedLength
                ? compress ? EffectScriptTiming.Scale(fixedLength, duration, fixedTotal) : fixedLength
                : EffectScriptTiming.Scale(remaining, EffectScriptTiming.Scale(new(1), segment.FlexWeight), EffectScriptTiming.Scale(new(1), flexTotal));
            foreach (var frame in segment.Keyframes)
            {
                var time = cursor + EffectScriptTiming.Scale(length, frame.Progress);
                switch (frame.Property)
                {
                    case EffectScriptProperty.POSITION:
                        Add(tracks, AnimationProperty.POSITION, frame, time, target.Transform.Position, origin);
                        break;
                    case EffectScriptProperty.SCALE:
                        Add(tracks, AnimationProperty.SCALE, frame, time, target.Transform.Scale, origin);
                        break;
                    case EffectScriptProperty.ROTATION:
                        Add(tracks, AnimationProperty.ROTATION, frame, time, target.Transform.Rotation, origin);
                        break;
                    case EffectScriptProperty.OPACITY:
                        Add(tracks, AnimationProperty.OPACITY, frame, time, target.Opacity, origin);
                        break;
                    case EffectScriptProperty.BLUR:
                        Add(tracks, AnimationProperty.BLUR, frame, time, target.Blur, origin);
                        break;
                    case EffectScriptProperty.STROKE_WIDTH:
                        Add(tracks, AnimationProperty.STROKE_WIDTH, frame, time, subtitleStyle?.StrokeWidth ?? target.StrokeWidth, origin);
                        break;
                    case EffectScriptProperty.FILL:
                        Add(tracks, AnimationProperty.FILL, frame, time, subtitleStyle?.Fill ?? target.Fill, origin);
                        break;
                    case EffectScriptProperty.STROKE:
                        Add(tracks, AnimationProperty.STROKE, frame, time, subtitleStyle?.Stroke ?? target.Stroke, origin);
                        break;
                    case EffectScriptProperty.PATH_PROGRESS:
                        Add(tracks, AnimationProperty.PATH_PROGRESS, frame, time, 0, origin);
                        break;
                    default:
                        throw new EffectScriptException("未知脚本属性。", frame.Line, frame.Column);
                }
            }

            cursor += length;
        }

        return tracks.OrderBy(pair => pair.Key).Select(pair =>
        {
            var frames = pair.Value;
            if (frames[^1].Time < end)
            {
                frames[^1] = frames[^1] with { Interpolation = KeyframeInterpolation.HOLD };
                frames.Add(new(end, frames[^1].Value, KeyframeInterpolation.HOLD));
            }

            return new AnimationTrack(pair.Key, frames.ToImmutableArray());
        }).ToImmutableArray();
    }

    private static double Resolve(EffectScriptValueKind kind, double baseValue, double literal)
    {
        return kind switch
        {
            EffectScriptValueKind.BASE => baseValue,
            EffectScriptValueKind.OFFSET => baseValue + literal,
            EffectScriptValueKind.FACTOR => baseValue * literal,
            _ => literal
        };
    }

    private static void Add(Dictionary<AnimationProperty, List<Keyframe>> tracks, AnimationProperty property,
        EffectScriptKeyframe source, MediaTime time, AnimationValue baseValue, MediaTime origin)
    {
        var value = baseValue;
        if (source.Value.Kind != EffectScriptValueKind.BASE)
        {
            var literal = source.Value.Literal!.Value;
            for (var component = 0; component < value.ComponentCount; component++)
            {
                value = value.WithComponent(component, Resolve(source.Value.Kind, baseValue.GetComponent(component), literal.GetComponent(component)));
            }
        }

        for (var component = 0; component < value.ComponentCount; component++)
        {
            var number = value.GetComponent(component);
            if (!double.IsFinite(number) || number < AnimationPropertyMetadata.GetMinimum(property, component) ||
                number > AnimationPropertyMetadata.GetMaximum(property, component))
            {
                throw new EffectScriptException($"{source.Property} 的求值结果超出项目允许范围。", source.Line, source.Column);
            }
        }

        if (!tracks.TryGetValue(property, out var frames))
        {
            frames = [];
            tracks.Add(property, frames);
            if (time > origin)
            {
                frames.Add(new(origin, baseValue, KeyframeInterpolation.HOLD));
            }
        }

        var key = new Keyframe(time, value, source.Interpolation);
        if (frames.Count > 0 && frames[^1].Time == time)
        {
            if (!frames[^1].Value.Equals(value))
            {
                throw new EffectScriptException($"{source.Property} 的共享端点存在不同值，零时长段也不能产生跳变。", source.Line, source.Column);
            }

            frames[^1] = key;
        }
        else
        {
            if (source.Progress == 0 && frames.Count > 0)
            {
                if (!frames[^1].Value.Equals(value))
                {
                    throw new EffectScriptException($"{source.Property} 在未声明区间后发生跳变，请显式声明连续关键帧。", source.Line, source.Column);
                }

                frames[^1] = frames[^1] with { Interpolation = KeyframeInterpolation.HOLD };
            }

            frames.Add(key);
        }
    }
}
