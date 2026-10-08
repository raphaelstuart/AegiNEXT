namespace AegiNext.Desktop.Settings.Tasks;

/// <summary>携带已经确认的任务并行上限。</summary>
public sealed class TaskSettingsChangedEventArgs(int maximumConcurrentTasks) : EventArgs
{
    public int MaximumConcurrentTasks { get; } = maximumConcurrentTasks;
}
