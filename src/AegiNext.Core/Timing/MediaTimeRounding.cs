namespace AegiNext.Core.Timing;

/// <summary>
/// 时间量化策略：TO_EVEN 就近取整且中点取偶数，TOWARD_ZERO 向零，FLOOR 向负无穷，CEILING 向正无穷。
/// </summary>
public enum MediaTimeRounding
{
    TO_EVEN,
    TOWARD_ZERO,
    FLOOR,
    CEILING
}
