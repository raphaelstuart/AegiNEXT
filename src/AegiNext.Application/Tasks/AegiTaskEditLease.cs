namespace AegiNext.Application.Tasks;

/// <summary>Restricts project editing until disposal or final task cleanup.</summary>
public sealed class AegiTaskEditLease : IDisposable
{
    private Action? release;

    internal AegiTaskEditLease(Action release)
    {
        this.release = release;
    }

    /// <summary>Releases this lease once.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
