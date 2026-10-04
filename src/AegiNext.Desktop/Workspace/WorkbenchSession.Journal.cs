using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal WorkbenchLogJournal Journal { get; } = new();

    internal void LogInfo(string source, string message, string? details = null)
    {
        Journal.Append(WorkbenchLogLevel.INFO, source, message, details);
    }

    internal void LogWarning(string source, string message, string? details = null)
    {
        Journal.Append(WorkbenchLogLevel.WARNING, source, message, details);
    }

    internal void LogError(string source, Exception error)
    {
        Journal.ReportError(source, error);
    }

    internal void SetDiagnosticError(string source, Exception? error)
    {
        Journal.SetDiagnosticError(source, error);
    }

    private void DisposeJournal()
    {
        ViewModel.Log.Dispose();
        Journal.Dispose();
    }
}
