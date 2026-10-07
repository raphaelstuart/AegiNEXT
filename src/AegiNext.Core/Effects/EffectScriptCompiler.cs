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
        return CompileWithCoverage(script, target, subtitleStyle).Tracks;
    }

    internal static EffectScriptCompilation CompileWithCoverage(EffectScript script, ProjectLayer target, SubtitleStyle? subtitleStyle)
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

    private static EffectScriptCompilation CompileCore(EffectScript script, ProjectLayer target, SubtitleStyle? subtitleStyle)
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
        var tracks = new Dictionary<AnimationTrackTarget, List<Keyframe>>();
        var intervals = ImmutableArray.CreateBuilder<EffectScriptInterval>();
        var cursor = origin;
        foreach (var segment in script.Segments)
        {
            var length = segment.FixedDuration is { } fixedLength
                ? compress ? EffectScriptTiming.Scale(fixedLength, duration, fixedTotal) : fixedLength
                : EffectScriptTiming.Scale(remaining, EffectScriptTiming.Scale(new(1), segment.FlexWeight), EffectScriptTiming.Scale(new(1), flexTotal));
            foreach (var frame in segment.Keyframes)
            {
                var time = cursor + EffectScriptTiming.Scale(length, frame.Progress);
                var animationTarget = ResolveTarget(frame, target);
                Add(tracks, animationTarget, frame, time, ResolveBaseValue(frame, animationTarget, target, subtitleStyle), origin);
                if (frame.Progress == 0)
                {
                    intervals.Add(new(animationTarget, cursor, cursor + length, frame.Line, frame.Column));
                }
            }

            cursor += length;
        }

        var compiled = tracks.OrderBy(pair => pair.Key.Property).ThenBy(pair => pair.Key.NodeId).Select(pair =>
        {
            var frames = pair.Value;
            if (frames[^1].Time < end)
            {
                frames[^1] = frames[^1] with { Interpolation = KeyframeInterpolation.HOLD };
                frames.Add(new(end, frames[^1].Value, KeyframeInterpolation.HOLD));
            }

            return new AnimationTrack(pair.Key, frames.ToImmutableArray());
        }).ToImmutableArray();
        return new(compiled, intervals.ToImmutable());
    }

    private static AnimationTrackTarget ResolveTarget(EffectScriptKeyframe frame, ProjectLayer layer)
    {
        var property = EffectScriptPropertyMetadata.GetAnimationProperty(frame.Property);
        if (!AnimationPropertyMetadata.IsNodeProperty(property))
        {
            return new(property);
        }

        var selector = frame.NodeSelector!.Value;
        if (layer.Mask is not VectorClipMask vector || selector.ContourNumber > vector.Contours.Length ||
            selector.NodeNumber > vector.Contours[selector.ContourNumber - 1].Nodes.Length)
        {
            throw new EffectScriptException($"蒙版轮廓 {selector.ContourNumber} 的节点 {selector.NodeNumber} 不存在。", frame.Line, frame.Column);
        }

        return new(property, vector.Contours[selector.ContourNumber - 1].Nodes[selector.NodeNumber - 1].Id);
    }

    private static AnimationValue ResolveBaseValue(EffectScriptKeyframe frame, AnimationTrackTarget animationTarget,
        ProjectLayer layer, SubtitleStyle? subtitleStyle)
    {
        if (AnimationPropertyMetadata.IsMaskProperty(animationTarget.Property))
        {
            if (layer.Mask is not { } mask)
            {
                throw new EffectScriptException("目标 Clip 尚无蒙版；脚本只动画现有几何。", frame.Line, frame.Column);
            }

            try
            {
                return ClipMaskAnimation.GetBaseValue(mask, animationTarget);
            }
            catch (InvalidDataException error)
            {
                throw new EffectScriptException(error.Message, frame.Line, frame.Column, error);
            }
        }

        return animationTarget.Property switch
        {
            AnimationProperty.POSITION => layer.Transform.Position,
            AnimationProperty.SCALE => layer.Transform.Scale,
            AnimationProperty.ROTATION => layer.Transform.Rotation,
            AnimationProperty.OPACITY => layer.Opacity,
            AnimationProperty.BLUR => layer.Blur,
            AnimationProperty.STROKE_WIDTH => subtitleStyle?.StrokeWidth ?? layer.StrokeWidth,
            AnimationProperty.FILL => subtitleStyle?.Fill ?? layer.Fill,
            AnimationProperty.STROKE => subtitleStyle?.Stroke ?? layer.Stroke,
            AnimationProperty.PATH_PROGRESS => 0,
            _ => throw new EffectScriptException("未知脚本属性。", frame.Line, frame.Column)
        };
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

    private static void Add(Dictionary<AnimationTrackTarget, List<Keyframe>> tracks, AnimationTrackTarget target,
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
            if (!double.IsFinite(number) || number < AnimationPropertyMetadata.GetMinimum(target.Property, component) ||
                number > AnimationPropertyMetadata.GetMaximum(target.Property, component))
            {
                throw new EffectScriptException($"{source.Property} 的求值结果超出项目允许范围。", source.Line, source.Column);
            }
        }

        if (!tracks.TryGetValue(target, out var frames))
        {
            frames = [];
            tracks.Add(target, frames);
            if (time > origin)
            {
                frames.Add(new(origin, baseValue, KeyframeInterpolation.HOLD));
            }
        }

        var key = new Keyframe(time, value, source.Interpolation) { Exponent = source.Exponent };
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
