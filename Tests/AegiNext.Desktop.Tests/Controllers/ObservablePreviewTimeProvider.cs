namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class ObservablePreviewTimeProvider : TimeProvider
{
    private const long AUTO_ADVANCE_TICKS = 10;
    private readonly Lock gate = new();
    private readonly List<ObservablePreviewTimer> timers = [];
    private TaskCompletionSource scheduleChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long scheduleRevision;
    private long advanceCount;
    private long ticks;

    /// <summary>使用 TimeSpan 刻度作为模拟单调时钟的频率。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    internal long ScheduleRevision
    {
        get
        {
            lock (gate)
            {
                return scheduleRevision;
            }
        }
    }

    internal bool HasScheduledTimer
    {
        get
        {
            lock (gate)
            {
                return timers.Any(timer => !timer.IsDisposed && timer.NextTick != long.MaxValue);
            }
        }
    }

    internal string Diagnostics
    {
        get
        {
            lock (gate)
            {
                var armed = timers.Where(timer => !timer.IsDisposed && timer.NextTick != long.MaxValue).ToArray();
                var next = armed.Select(timer => timer.NextTick).DefaultIfEmpty(long.MaxValue).Min();
                return $"ClockTicks={ticks}; ScheduleRevision={scheduleRevision}; Advances={advanceCount}; " +
                    $"ArmedTimers={armed.Length}; EarliestDueTicks={next}";
            }
        }
    }

    /// <summary>每次读取推进一微秒，模拟没有定时器的亚毫秒等待期间自然流逝的时间。</summary>
    public override long GetTimestamp()
    {
        lock (gate)
        {
            ticks = checked(ticks + AUTO_ADVANCE_TICKS);
            return ticks;
        }
    }

    /// <summary>返回以 Unix 起点和模拟刻度组成的时间。</summary>
    public override DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    }

    /// <summary>安排仅在测试显式推进模拟时钟时触发的定时器。</summary>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ObservablePreviewTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
        }
        timer.Change(dueTime, period);
        return timer;
    }

    internal Task WaitForScheduleChangeAsync(long revision)
    {
        lock (gate)
        {
            return scheduleRevision > revision ? Task.CompletedTask : scheduleChanged.Task;
        }
    }

    internal bool AdvanceToNextTimer(TimeSpan lateness)
    {
        ObservablePreviewTimer[] pending;
        lock (gate)
        {
            var next = timers.Where(timer => !timer.IsDisposed).Select(timer => timer.NextTick).DefaultIfEmpty(long.MaxValue).Min();
            if (next == long.MaxValue)
            {
                return false;
            }
            ticks = checked(Math.Max(ticks, next) + lateness.Ticks);
            advanceCount++;
            pending = TakeDueTimersUnderLock();
        }
        foreach (var timer in pending)
        {
            timer.Invoke();
        }
        return true;
    }

    internal void Advance(TimeSpan elapsed)
    {
        ObservablePreviewTimer[] pending;
        lock (gate)
        {
            ticks = checked(ticks + elapsed.Ticks);
            pending = TakeDueTimersUnderLock();
        }
        foreach (var timer in pending)
        {
            timer.Invoke();
        }
    }

    internal bool Schedule(ObservablePreviewTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            if (timer.IsDisposed)
            {
                return false;
            }
            timer.NextTick = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(ticks + dueTime.Ticks);
            timer.PeriodTicks = period > TimeSpan.Zero ? period.Ticks : 0;
            if (timer.NextTick != long.MaxValue)
            {
                scheduleRevision++;
                var notification = scheduleChanged;
                scheduleChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
                notification.TrySetResult();
            }
            return true;
        }
    }

    internal void Remove(ObservablePreviewTimer timer)
    {
        lock (gate)
        {
            timer.IsDisposed = true;
            timer.NextTick = long.MaxValue;
            timers.Remove(timer);
        }
    }

    private ObservablePreviewTimer[] TakeDueTimersUnderLock()
    {
        var pending = timers.Where(timer => !timer.IsDisposed && timer.NextTick <= ticks).ToArray();
        foreach (var timer in pending)
        {
            timer.NextTick = timer.PeriodTicks == 0 ? long.MaxValue : checked(ticks + timer.PeriodTicks);
        }
        return pending;
    }
}
