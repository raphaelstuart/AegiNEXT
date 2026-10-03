namespace AegiNext.Core.Timing;

/// <summary>
/// 保留原始 PTS 和时基的媒体时间戳；相等表示相同来源数值，而不是仅表示相同时刻。
/// </summary>
public sealed record MediaTimestamp
{
    /// <summary>
    /// 创建原始时间戳；原生库的未定义 PTS 标记必须先由媒体适配器识别。
    /// </summary>
    public MediaTimestamp(long value, MediaTimeBase timeBase)
    {
        ArgumentNullException.ThrowIfNull(timeBase);
        Value = value;
        TimeBase = timeBase;
    }

    public long Value { get; }

    public MediaTimeBase TimeBase { get; }

    /// <summary>
    /// 精确转换为有理秒，不修改原始 PTS。
    /// </summary>
    public MediaTime ToMediaTime()
    {
        return new MediaTime(TimeBase.Numerator, TimeBase.Denominator) * Value;
    }
}
