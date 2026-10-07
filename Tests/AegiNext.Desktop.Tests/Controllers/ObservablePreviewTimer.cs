namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class ObservablePreviewTimer(ObservablePreviewTimeProvider clock, TimerCallback callback, object? state) : ITimer
{
    internal long NextTick { get; set; } = long.MaxValue;
    internal long PeriodTicks { get; set; }
    internal bool IsDisposed { get; set; }

    /// <summary>相对当前模拟时钟重新安排触发时间和周期。</summary>
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return clock.Schedule(this, dueTime, period);
    }

    /// <summary>从模拟时钟中移除定时器。</summary>
    public void Dispose()
    {
        clock.Remove(this);
    }

    /// <summary>同步移除模拟定时器并返回已完成的释放操作。</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void Invoke()
    {
        callback(state);
    }
}
