namespace AegiNext.Desktop.Workspace.Diagnostics;

internal sealed class WorkbenchLogJournal : IDisposable
{
    public const int CAPACITY = 2000;
    private readonly object gate = new();
    private readonly Queue<WorkbenchLogEntry> entries = new();
    private readonly Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);
    private long sequence;
    private long readThrough;
    private bool disposed;

    internal event EventHandler? Changed;

    public IReadOnlyList<WorkbenchLogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    public int UnreadErrorCount
    {
        get
        {
            lock (gate)
            {
                return entries.Count(entry => entry.Sequence > readThrough && entry.Level == WorkbenchLogLevel.ERROR);
            }
        }
    }

    internal void Append(WorkbenchLogLevel level, string source, string message, string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AppendCore(level, source, message, details);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void ReportError(string source, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Append(WorkbenchLogLevel.ERROR, source, error.Message, error.ToString());
    }

    internal void SetDiagnosticError(string source, Exception? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (error is null)
            {
                diagnostics.Remove(source);
                return;
            }

            var details = error.ToString();
            if (diagnostics.TryGetValue(source, out var previous) && previous == details)
            {
                return;
            }

            diagnostics[source] = details;
            AppendCore(WorkbenchLogLevel.ERROR, source, error.Message, details);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void MarkRead()
    {
        lock (gate)
        {
            if (disposed || readThrough == sequence)
            {
                return;
            }
            readThrough = sequence;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Clear()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            entries.Clear();
            readThrough = sequence;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            Changed = null;
        }
    }

    private void AppendCore(WorkbenchLogLevel level, string source, string message, string? details)
    {
        entries.Enqueue(new(++sequence, DateTimeOffset.UtcNow, level, source, message, details));
        while (entries.Count > CAPACITY)
        {
            entries.Dequeue();
        }
    }
}
