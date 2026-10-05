using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>动画属性的稳定维度、分量名称及数值范围；不包含界面或本地化依赖。</summary>
public static class AnimationPropertyMetadata
{
    /// <summary>返回面板及脚本可使用的完整属性顺序，不包含旧分量。</summary>
    public static ImmutableArray<AnimationProperty> CurrentProperties { get; } =
    [
        AnimationProperty.POSITION, AnimationProperty.SCALE, AnimationProperty.ROTATION, AnimationProperty.OPACITY,
        AnimationProperty.FILL, AnimationProperty.STROKE, AnimationProperty.STROKE_WIDTH, AnimationProperty.BLUR,
        AnimationProperty.PATH_PROGRESS
    ];

    /// <summary>获取一个稳定属性的值类型，旧分量属性仍返回标量供显式迁移使用。</summary>
    public static AnimationValueKind GetValueKind(AnimationProperty property) => property switch
    {
        AnimationProperty.POSITION or AnimationProperty.SCALE => AnimationValueKind.VECTOR,
        AnimationProperty.FILL or AnimationProperty.STROKE => AnimationValueKind.COLOR,
        _ when Enum.IsDefined(property) => AnimationValueKind.SCALAR,
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };

    /// <summary>获取属性分量数量。</summary>
    public static int GetComponentCount(AnimationProperty property) => GetValueKind(property) switch
    {
        AnimationValueKind.VECTOR => 2,
        AnimationValueKind.COLOR => 4,
        _ => 1
    };

    /// <summary>获取分量稳定名称，标量属性返回空名称。</summary>
    public static string GetComponentName(AnimationProperty property, int component = 0)
    {
        ValidateComponent(property, component);
        return GetValueKind(property) switch
        {
            AnimationValueKind.VECTOR => component == 0 ? "X" : "Y",
            AnimationValueKind.COLOR => component switch { 0 => "R", 1 => "G", 2 => "B", _ => "A" },
            _ => string.Empty
        };
    }

    /// <summary>获取分量允许的最小值。</summary>
    public static double GetMinimum(AnimationProperty property, int component = 0)
    {
        ValidateComponent(property, component);
        return property switch
        {
            AnimationProperty.FILL or AnimationProperty.STROKE => component == 3 ? 0 : -65504,
            AnimationProperty.OPACITY or AnimationProperty.FILL_ALPHA or AnimationProperty.STROKE_ALPHA or
                AnimationProperty.PATH_PROGRESS or AnimationProperty.BLUR or AnimationProperty.STROKE_WIDTH => 0,
            AnimationProperty.SCALE or AnimationProperty.SCALE_X or AnimationProperty.SCALE_Y => -10000,
            AnimationProperty.FILL_RED or AnimationProperty.FILL_GREEN or AnimationProperty.FILL_BLUE or
                AnimationProperty.STROKE_RED or AnimationProperty.STROKE_GREEN or AnimationProperty.STROKE_BLUE => -65504,
            _ => -1e9
        };
    }

    /// <summary>获取分量允许的最大值。</summary>
    public static double GetMaximum(AnimationProperty property, int component = 0)
    {
        ValidateComponent(property, component);
        return property switch
        {
            AnimationProperty.FILL or AnimationProperty.STROKE => component == 3 ? 1 : 65504,
            AnimationProperty.OPACITY or AnimationProperty.FILL_ALPHA or AnimationProperty.STROKE_ALPHA or AnimationProperty.PATH_PROGRESS => 1,
            AnimationProperty.BLUR => 512,
            AnimationProperty.STROKE_WIDTH => 4096,
            AnimationProperty.SCALE or AnimationProperty.SCALE_X or AnimationProperty.SCALE_Y => 10000,
            AnimationProperty.FILL_RED or AnimationProperty.FILL_GREEN or AnimationProperty.FILL_BLUE or
                AnimationProperty.STROKE_RED or AnimationProperty.STROKE_GREEN or AnimationProperty.STROKE_BLUE => 65504,
            _ => 1e9
        };
    }

    /// <summary>判断是否为应在载入或旧预设应用时迁移的分量属性。</summary>
    public static bool IsLegacyComponent(AnimationProperty property) => property is
        AnimationProperty.POSITION_X or AnimationProperty.POSITION_Y or AnimationProperty.SCALE_X or AnimationProperty.SCALE_Y or
        AnimationProperty.FILL_RED or AnimationProperty.FILL_GREEN or AnimationProperty.FILL_BLUE or AnimationProperty.FILL_ALPHA or
        AnimationProperty.STROKE_RED or AnimationProperty.STROKE_GREEN or AnimationProperty.STROKE_BLUE or AnimationProperty.STROKE_ALPHA;

    /// <summary>获取完整属性对应的旧分量属性，顺序与值分量一致。</summary>
    public static ImmutableArray<AnimationProperty> GetLegacyComponents(AnimationProperty property) => property switch
    {
        AnimationProperty.POSITION => [AnimationProperty.POSITION_X, AnimationProperty.POSITION_Y],
        AnimationProperty.SCALE => [AnimationProperty.SCALE_X, AnimationProperty.SCALE_Y],
        AnimationProperty.FILL => [AnimationProperty.FILL_RED, AnimationProperty.FILL_GREEN, AnimationProperty.FILL_BLUE, AnimationProperty.FILL_ALPHA],
        AnimationProperty.STROKE => [AnimationProperty.STROKE_RED, AnimationProperty.STROKE_GREEN, AnimationProperty.STROKE_BLUE, AnimationProperty.STROKE_ALPHA],
        _ => []
    };

    private static void ValidateComponent(AnimationProperty property, int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, GetComponentCount(property));
    }
}
