namespace AegiNext.Desktop.Ui.Tests;

internal sealed class AdvancingPlaybackTimeProvider : TimeProvider
{
    private readonly ManualPlaybackTimeProvider clock = new();
    private long timestampOffset;

    /// <summary>以 TimeSpan 刻度表示模拟播放时钟。</summary>
    public override long TimestampFrequency => clock.TimestampFrequency;

    /// <summary>每次读取推进一个刻度，暴露同一次判定中的重复时间采样。</summary>
    public override long GetTimestamp()
    {
        return clock.GetTimestamp() + Interlocked.Increment(ref timestampOffset);
    }

    /// <summary>返回模拟时钟的绝对时间。</summary>
    public override DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    }

    /// <summary>沿用手动时钟的定时器，避免读取时间触发后台回调。</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        return clock.CreateTimer(callback, state, dueTime, period);
    }
}
