namespace AegiNext.Desktop.Workspace;

internal sealed record ProjectOpenResult
{
    internal ProjectOpenResult(ProjectOpenStatus status, Exception? error = null,
        IReadOnlyList<Exception>? diagnostics = null)
    {
        Status = status;
        Error = error;
        Diagnostics = diagnostics ?? [];
    }

    internal ProjectOpenStatus Status { get; }
    internal Exception? Error { get; }
    internal IReadOnlyList<Exception> Diagnostics { get; }
}
