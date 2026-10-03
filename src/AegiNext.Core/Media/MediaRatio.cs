using System.Numerics;

namespace AegiNext.Core.Media;

/// <summary>
/// 无单位的规范化有理数，用于帧率、像素比例、色度与亮度元数据，不代替媒体时基。
/// </summary>
public sealed record MediaRatio
{
    /// <summary>
    /// 创建精确比值；分母必须为正，约分不使用浮点数。
    /// </summary>
    public MediaRatio(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        var divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public long Numerator { get; }

    public long Denominator { get; }
}
