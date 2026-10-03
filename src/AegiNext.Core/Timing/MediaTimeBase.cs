namespace AegiNext.Core.Timing;

/// <summary>
/// 每个媒体刻度对应的正有理秒数；不使用帧率代替媒体时基。
/// </summary>
public sealed record MediaTimeBase
{
    /// <summary>
    /// 创建并规范化严格为正的媒体时基。
    /// </summary>
    public MediaTimeBase(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        var time = new MediaTime(numerator, denominator);
        Numerator = time.Numerator;
        Denominator = time.Denominator;
    }

    public static MediaTimeBase TimeSpanTicks { get; } = new(1, TimeSpan.TicksPerSecond);

    public long Numerator { get; }

    public long Denominator { get; }
}
