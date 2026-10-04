using System.Globalization;

namespace AegiNext.Desktop.Workspace.Diagnostics;

internal sealed record WorkbenchLogEntry(long Sequence, DateTimeOffset Timestamp, WorkbenchLogLevel Level,
    string Source, string Message, string? Details)
{
    public string Summary => string.Create(CultureInfo.InvariantCulture,
        $"{Timestamp.ToLocalTime():HH:mm:ss.fff}  {Level}  [{Source}] {Message}");
    public string FullText => string.Create(CultureInfo.InvariantCulture,
        $"{Timestamp:O}  {Level}  [{Source}] {Message}") +
        (string.IsNullOrEmpty(Details) ? string.Empty : Environment.NewLine + Details);
}
