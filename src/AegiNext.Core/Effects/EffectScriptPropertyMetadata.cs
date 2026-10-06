using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>声明式属性到通用动画属性的共享映射及编辑器语法名称。</summary>
public static class EffectScriptPropertyMetadata
{
    public static ImmutableArray<string> PropertyNames { get; } =
    [
        "position", "scale", "rotation", "opacity", "blur", "stroke-width", "path-progress", "fill", "stroke",
        "mask-rectangle-top-left", "mask-rectangle-bottom-right", "mask-position", "mask-scale", "mask-rotation",
        "mask-node(1,1).position", "mask-node(1,1).in-handle", "mask-node(1,1).out-handle"
    ];

    /// <summary>识别编辑器中的属性名称；节点序号的语法及存在性由解析器和编译器验证。</summary>
    public static bool TryGetProperty(string name, out EffectScriptProperty property)
    {
        var index = PropertyNames.IndexOf(name);
        if (index >= 0)
        {
            property = (EffectScriptProperty)index;
            return true;
        }

        if (name.StartsWith("mask-node(", StringComparison.Ordinal))
        {
            foreach (var (suffix, value) in new[]
            {
                (".position", EffectScriptProperty.MASK_NODE_POSITION),
                (".in-handle", EffectScriptProperty.MASK_NODE_IN_HANDLE),
                (".out-handle", EffectScriptProperty.MASK_NODE_OUT_HANDLE)
            })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    property = value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    /// <summary>返回完整脚本属性对应的持久化动画属性。</summary>
    public static AnimationProperty GetAnimationProperty(EffectScriptProperty property) => property switch
    {
        EffectScriptProperty.POSITION => AnimationProperty.POSITION,
        EffectScriptProperty.SCALE => AnimationProperty.SCALE,
        EffectScriptProperty.ROTATION => AnimationProperty.ROTATION,
        EffectScriptProperty.OPACITY => AnimationProperty.OPACITY,
        EffectScriptProperty.BLUR => AnimationProperty.BLUR,
        EffectScriptProperty.STROKE_WIDTH => AnimationProperty.STROKE_WIDTH,
        EffectScriptProperty.PATH_PROGRESS => AnimationProperty.PATH_PROGRESS,
        EffectScriptProperty.FILL => AnimationProperty.FILL,
        EffectScriptProperty.STROKE => AnimationProperty.STROKE,
        EffectScriptProperty.MASK_RECTANGLE_TOP_LEFT => AnimationProperty.MASK_RECTANGLE_TOP_LEFT,
        EffectScriptProperty.MASK_RECTANGLE_BOTTOM_RIGHT => AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT,
        EffectScriptProperty.MASK_POSITION => AnimationProperty.MASK_POSITION,
        EffectScriptProperty.MASK_SCALE => AnimationProperty.MASK_SCALE,
        EffectScriptProperty.MASK_ROTATION => AnimationProperty.MASK_ROTATION,
        EffectScriptProperty.MASK_NODE_POSITION => AnimationProperty.MASK_NODE_POSITION,
        EffectScriptProperty.MASK_NODE_IN_HANDLE => AnimationProperty.MASK_NODE_IN_HANDLE,
        EffectScriptProperty.MASK_NODE_OUT_HANDLE => AnimationProperty.MASK_NODE_OUT_HANDLE,
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };
}
