namespace AegiNext.Desktop.Tests;

internal sealed class ManualPlaybackTimer(ManualPlaybackTimeProvider clock, TimerCallback callback, object? state) : ITimer
{
    private readonly Lock gate = new();
    private long nextTick = long.MaxValue;
    private long periodTicks;
    private bool disposed;

    internal bool IsArmed
    {
        get
        {
            lock (gate)
            {
                return !disposed && nextTick != long.MaxValue;
            }
        }
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        var current = clock.GetTimestamp();
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }

            nextTick = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(current + dueTime.Ticks);
            periodTicks = period > TimeSpan.Zero ? period.Ticks : 0;
            return true;
        }
    }

    internal void FireIfDue(long current)
    {
        lock (gate)
        {
            if (disposed || nextTick > current)
            {
                return;
            }

            nextTick = periodTicks == 0 ? long.MaxValue : checked(current + periodTicks);
        }

        callback(state);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            nextTick = long.MaxValue;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

