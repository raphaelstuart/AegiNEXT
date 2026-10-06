namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectPersistencePauseLease(Action resume) : IAsyncDisposable
{
    private Action? release = resume;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
        return ValueTask.CompletedTask;
    }
}
