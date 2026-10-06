namespace AegiNext.Media.Tests.Playback;

internal sealed class ManualPlaybackTimeProvider : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ManualPlaybackTimer> timers = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return ticks;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualPlaybackTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    internal int ActiveTimerCount
    {
        get
        {
            lock (gate)
            {
                return timers.Count(timer => timer.IsArmed);
            }
        }
    }

    internal void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        ManualPlaybackTimer[] pending;
        long current;
        lock (gate)
        {
            ticks = checked(ticks + elapsed.Ticks);
            current = ticks;
            pending = timers.ToArray();
        }

        foreach (var timer in pending)
        {
            timer.FireIfDue(current);
        }
    }
}
