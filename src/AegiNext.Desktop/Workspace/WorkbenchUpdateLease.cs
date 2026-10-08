namespace AegiNext.Desktop.Workspace;

internal sealed class WorkbenchUpdateLease(Action release) : IDisposable
{
    private Action? release = release;

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
