namespace AegiNext.Desktop.Workspace;

internal sealed class WorkbenchUpdateState
{
    private int depth;

    internal bool IsActive => Volatile.Read(ref depth) > 0;

    internal WorkbenchUpdateLease Acquire()
    {
        Interlocked.Increment(ref depth);
        return new(() => Interlocked.Decrement(ref depth));
    }
}
