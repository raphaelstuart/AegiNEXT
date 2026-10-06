using AegiNext.Core.Timing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>验证脚本数据接口和文本解析共享的结构、维度与资源预算。</summary>
public static class EffectScriptValidator
{
    public const int MAXIMUM_SEGMENTS = 128;
    public const int MAXIMUM_KEYFRAMES = 4096;
    public const int MAXIMUM_FIXED_SECONDS = 86400;

    /// <summary>验证所有时间段；不解析目标片段的基础值，不修改输入。</summary>
    public static void Validate(EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!IsIdentifier(script.Id) || !Enum.IsDefined(script.ShortClipPolicy) ||
            script.Segments.IsDefaultOrEmpty || script.Segments.Length > MAXIMUM_SEGMENTS)
        {
            throw new EffectScriptException("脚本标识、短片段规则或时间段集合无效。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var totalKeys = 0;
        var hasFlex = false;
        foreach (var segment in script.Segments)
        {
            if (segment is null)
            {
                throw new EffectScriptException("时间段不能为空。");
            }

            if (!IsIdentifier(segment.Name) || !names.Add(segment.Name) || segment.Keyframes.IsDefault)
            {
                throw new EffectScriptException("时间段名称无效、重复或关键帧集合缺失。", segment.Line, segment.Column);
            }

            if (segment.FixedDuration is { } duration)
            {
                if (duration <= MediaTime.Zero || duration > new MediaTime(MAXIMUM_FIXED_SECONDS) || segment.FlexWeight != 0)
                {
                    throw new EffectScriptException("固定段时长必须大于零且不超过 24 小时，不能同时设置自由权重。", segment.Line, segment.Column);
                }
            }
            else
            {
                hasFlex = true;
                if (segment.FlexWeight <= 0 || segment.FlexWeight > 1000 || decimal.Round(segment.FlexWeight, 6) != segment.FlexWeight)
                {
                    throw new EffectScriptException("自由段权重必须大于零、不超过 1000，且最多六位小数。", segment.Line, segment.Column);
                }
            }

            totalKeys += segment.Keyframes.Length;
            if (totalKeys > MAXIMUM_KEYFRAMES)
            {
                throw new EffectScriptException("关键帧数量超过预算。", segment.Line, segment.Column);
            }

            var previous = new Dictionary<(EffectScriptProperty Property, EffectScriptNodeSelector? Selector), decimal>();
            foreach (var frame in segment.Keyframes)
            {
                if (frame is null || frame.Value is null)
                {
                    throw new EffectScriptException("关键帧或属性值不能为空。", segment.Line, segment.Column);
                }

                if (!Enum.IsDefined(frame.Property) || !Enum.IsDefined(frame.Interpolation) || frame.Progress is < 0 or > 1 ||
                    decimal.Round(frame.Progress, 6) != frame.Progress || !double.IsFinite(frame.Exponent) || frame.Exponent <= 0)
                {
                    throw new EffectScriptException("关键帧属性或插值无效，段内位置应为 0 到 1，最多六位小数。", frame.Line, frame.Column);
                }

                var animationProperty = EffectScriptPropertyMetadata.GetAnimationProperty(frame.Property);
                if (AnimationPropertyMetadata.IsNodeProperty(animationProperty) != frame.NodeSelector.HasValue ||
                    frame.NodeSelector is { } selector &&
                    (selector.ContourNumber is < 1 or > 10000 || selector.NodeNumber is < 1 or > 10000))
                {
                    throw new EffectScriptException("只有蒙版节点属性携带一基轮廓及节点序号。", frame.Line, frame.Column);
                }

                var target = (frame.Property, frame.NodeSelector);
                if (previous.TryGetValue(target, out var last) ? frame.Progress <= last : frame.Progress != 0)
                {
                    throw new EffectScriptException("段内同一属性必须从 at 0 开始且位置严格递增。", frame.Line, frame.Column);
                }

                previous[target] = frame.Progress;
                var value = frame.Value;
                var expectedKind = AnimationPropertyMetadata.GetValueKind(animationProperty);
                if (!Enum.IsDefined(value.Kind) || (value.Kind == EffectScriptValueKind.BASE ? value.Literal.HasValue :
                    value.Literal is not { } literal || literal.Kind != expectedKind ||
                    Enumerable.Range(0, literal.ComponentCount).Any(component => !double.IsFinite(literal.GetComponent(component)))))
                {
                    throw new EffectScriptException("属性值维度不匹配或数值无效；位置和缩放需要二维向量，fill/stroke 需要完整线性 rgba，base 不带参数。",
                        frame.Line, frame.Column);
                }

                if (expectedKind == AnimationValueKind.COLOR)
                {
                    if (value.Kind is not (EffectScriptValueKind.ABSOLUTE or EffectScriptValueKind.BASE))
                    {
                        throw new EffectScriptException("fill/stroke 仅支持 rgba(r,g,b,a) 或 base，不支持 offset/factor。", frame.Line, frame.Column);
                    }

                    if (value.Literal is { } colorValue && Enumerable.Range(0, 4).Any(component =>
                        colorValue.GetComponent(component) < (component == 3 ? 0 : -65504) ||
                        colorValue.GetComponent(component) > (component == 3 ? 1 : 65504)))
                    {
                        throw new EffectScriptException("线性 rgba 的 RGB 必须在 ±65504 内，Alpha 必须在 0 到 1 内。", frame.Line, frame.Column);
                    }
                }

                if (frame.Property == EffectScriptProperty.PATH_PROGRESS && value.Kind != EffectScriptValueKind.ABSOLUTE)
                {
                    throw new EffectScriptException("path-progress 需要显式的 0 到 1 数值，没有固定基础值。", frame.Line, frame.Column);
                }
            }

            if (previous.Values.Any(progress => progress != 1))
            {
                throw new EffectScriptException("段内每个属性都必须以 at 1 结束。", segment.Line, segment.Column);
            }
        }

        if (!hasFlex || totalKeys == 0)
        {
            throw new EffectScriptException("脚本必须至少包含一个自由段和一个有关键帧的时间段。");
        }
    }

    private static bool IsIdentifier(string? value)
    {
        return value is { Length: > 0 and <= 64 } && char.IsAsciiLetterLower(value[0]) &&
            value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '-');
    }
}
