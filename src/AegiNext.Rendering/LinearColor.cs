namespace AegiNext.Rendering;

/// <summary>
/// 扩展线性 sRGB 的未预乘颜色；RGB 允许负值及超过 1 的亮度，默认值为透明黑。
/// </summary>
public readonly record struct LinearColor
{
    private const float MAX_HALF = 65504;

    /// <summary>
    /// 创建有限且可由 F16 表示的颜色；alpha 必须处于 [0, 1]。
    /// </summary>
    public LinearColor(float red, float green, float blue, float alpha)
    {
        ValidateChannel(red, nameof(red));
        ValidateChannel(green, nameof(green));
        ValidateChannel(blue, nameof(blue));
        RenderValidation.UnitInterval(alpha, nameof(alpha));
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public float Red { get; }
    public float Green { get; }
    public float Blue { get; }
    public float Alpha { get; }

    /// <summary>
    /// 将 [0, 1] 范围的 sRGB 编码色解码为线性颜色，alpha 不参与传递函数。
    /// </summary>
    public static LinearColor FromSrgb(float red, float green, float blue, float alpha)
    {
        RenderValidation.UnitInterval(red, nameof(red));
        RenderValidation.UnitInterval(green, nameof(green));
        RenderValidation.UnitInterval(blue, nameof(blue));
        return new(Decode(red), Decode(green), Decode(blue), alpha);
    }

    private static float Decode(float value)
    {
        return value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private static void ValidateChannel(float value, string parameter)
    {
        RenderValidation.Finite(value, parameter);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, -MAX_HALF, parameter);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MAX_HALF, parameter);
    }
}
