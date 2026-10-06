using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class WorkbenchLogJournalTests
{
    [Fact]
    public void BufferRetainsNewestTwoThousandEntriesAndCompleteExceptionDetails()
    {
        using var journal = new WorkbenchLogJournal();
        for (var index = 0; index < WorkbenchLogJournal.CAPACITY + 9; index++)
        {
            journal.Append(WorkbenchLogLevel.INFO, "Command", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var error = new InvalidOperationException(new string('x', 900), new IOException("Nested cause"));
        journal.ReportError("Export", error);
        Assert.Equal(WorkbenchLogJournal.CAPACITY, journal.Entries.Count);
        Assert.Equal("10", journal.Entries[0].Message);
        Assert.Equal(error.ToString(), journal.Entries[^1].Details);
        Assert.Contains("Nested cause", journal.Entries[^1].FullText);
        Assert.Equal(1, journal.UnreadErrorCount);
    }

    [Fact]
    public void PersistentDiagnosticsAppendOnChangeAndCanRecurAfterRecovery()
    {
        using var journal = new WorkbenchLogJournal();
        for (var index = 0; index < 50; index++)
        {
            journal.SetDiagnosticError("Playback", new IOException("Unavailable"));
        }
        Assert.Single(journal.Entries);
        journal.SetDiagnosticError("Audio", new IOException("Unavailable"));
        journal.SetDiagnosticError("Playback", new IOException("Changed cause"));
        journal.SetDiagnosticError("Playback", null);
        journal.SetDiagnosticError("Playback", new IOException("Unavailable"));
        Assert.Equal(4, journal.Entries.Count);
        journal.ReportError("Command", new IOException("Unavailable"));
        journal.ReportError("Command", new IOException("Unavailable"));
        Assert.Equal(6, journal.Entries.Count);
    }

    [Fact]
    public void ReadAndClearKeepDiagnosticSuppressionAndMonotonicSequence()
    {
        using var journal = new WorkbenchLogJournal();
        journal.SetDiagnosticError("Preview", new IOException("Failed"));
        var previous = Assert.Single(journal.Entries).Sequence;
        journal.MarkRead();
        Assert.Equal(0, journal.UnreadErrorCount);
        journal.Clear();
        journal.SetDiagnosticError("Preview", new IOException("Failed"));
        Assert.Empty(journal.Entries);
        journal.SetDiagnosticError("Preview", null);
        journal.SetDiagnosticError("Preview", new IOException("Failed"));
        Assert.True(Assert.Single(journal.Entries).Sequence > previous);
        Assert.Equal(1, journal.UnreadErrorCount);
    }

    [Fact]
    public async Task ConcurrentSourcesKeepUniqueOrderedEntries()
    {
        using var journal = new WorkbenchLogJournal();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(source => Task.Run(() =>
        {
            for (var index = 0; index < 80; index++)
            {
                journal.Append(WorkbenchLogLevel.WARNING, source.ToString(System.Globalization.CultureInfo.InvariantCulture), "Message");
            }
        })));
        Assert.Equal(640, journal.Entries.Count);
        Assert.Equal(Enumerable.Range(1, 640).Select(value => (long)value), journal.Entries.Select(entry => entry.Sequence));
    }
}
