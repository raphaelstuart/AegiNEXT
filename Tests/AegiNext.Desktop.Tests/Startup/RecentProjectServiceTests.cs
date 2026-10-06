using System.Text.Json;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Startup;

public sealed class RecentProjectServiceTests
{
    [Fact]
    public async Task MissingHistoryStartsEmptyWithoutCreatingAFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var service = new RecentProjectService(directory.Path);

        Assert.Empty(service.Entries);
        Assert.Null(service.LastError);
        Assert.True(service.Completion.IsCompletedSuccessfully);
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task RecordingNormalizesPathsDeduplicatesAndPersistsNamesAndUtcTimestamps()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var service = new RecentProjectService(directory.Path);
        var path = Path.Combine(directory.Path, "字幕 project 01.aeginext");
        var other = Path.Combine(directory.Path, "other.aeginext");
        var changes = 0;
        service.Changed += (_, _) => changes++;
        var started = DateTimeOffset.UtcNow;

        await service.RecordAsync(path);
        await service.RecordAsync(other);
        await service.RecordAsync(Path.Combine(directory.Path, "nested", "..", Path.GetFileName(path)));

        Assert.Equal(3, changes);
        Assert.Equal(2, service.Entries.Count);
        Assert.Equal(path, service.Entries[0].Path);
        Assert.Equal("字幕 project 01", service.Entries[0].Name);
        Assert.All(service.Entries, entry =>
        {
            Assert.Equal(TimeSpan.Zero, entry.LastUsedUtc.Offset);
            Assert.InRange(entry.LastUsedUtc, started, DateTimeOffset.UtcNow);
        });
        Assert.Null(service.LastError);
        await using var reopened = new RecentProjectService(directory.Path);
        Assert.Equal(service.Entries, reopened.Entries);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task ConcurrentRecordsRetainAtMostTwentyNewestEntriesAndFinishBeforeDisposal()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var service = new RecentProjectService(directory.Path);
        var writes = Enumerable.Range(0, 24)
            .Select(index => service.RecordAsync(Path.Combine(directory.Path, $"project-{index:D2}.aeginext")))
            .ToArray();

        await service.DisposeAsync();
        await Task.WhenAll(writes);

        Assert.Equal(20, service.Entries.Count);
        Assert.Equal("project-23", service.Entries[0].Name);
        Assert.Equal("project-04", service.Entries[^1].Name);
        Assert.Equal(service.Entries.OrderByDescending(entry => entry.LastUsedUtc), service.Entries);
        Assert.All(writes, write => Assert.True(write.IsCompletedSuccessfully));
        Assert.True(service.Completion.IsCompletedSuccessfully);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RecordAsync(Path.Combine(directory.Path, "new.aeginext")));
        await using var reopened = new RecentProjectService(directory.Path);
        Assert.Equal(service.Entries, reopened.Entries);
        Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public async Task LoadingSortsUtcTimestampsNormalizesDuplicatesAndKeepsMissingProjects()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var first = Path.Combine(directory.Path, "missing-first.aeginext");
        var second = Path.Combine(directory.Path, "missing-second.aeginext");
        var utc = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            new RecentProjectEntry(first, "old first", utc.AddMinutes(-1)),
            new RecentProjectEntry(second, "second", utc.ToOffset(TimeSpan.FromHours(8))),
            new RecentProjectEntry(Path.Combine(directory.Path, "nested", "..", "missing-first.aeginext"),
                "latest first", utc.AddMinutes(1))
        };
        var historyPath = Path.Combine(directory.Path, "recent-projects.json");
        var original = JsonSerializer.SerializeToUtf8Bytes(records);
        await File.WriteAllBytesAsync(historyPath, original);

        await using var service = new RecentProjectService(directory.Path);

        Assert.Null(service.LastError);
        Assert.Equal(new[] { first, second }, service.Entries.Select(entry => entry.Path));
        Assert.Equal("missing-first", service.Entries[0].Name);
        Assert.Equal(utc.AddMinutes(1), service.Entries[0].LastUsedUtc);
        Assert.All(service.Entries, entry => Assert.Equal(TimeSpan.Zero, entry.LastUsedUtc.Offset));
        Assert.All(service.Entries, entry => Assert.False(File.Exists(entry.Path)));
        Assert.Equal(original, await File.ReadAllBytesAsync(historyPath));
    }

    [Fact]
    public async Task PathIdentityIgnoresCaseOnlyOnWindows()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var service = new RecentProjectService(directory.Path);
        var upper = Path.Combine(directory.Path, "Project.aeginext");
        var lower = Path.Combine(directory.Path, "project.aeginext");

        await service.RecordAsync(upper);
        await service.RecordAsync(lower);

        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, service.Entries.Count);
        Assert.Equal(lower, service.Entries[0].Path);
    }

    [Fact]
    public async Task RemoveDeletesOnlyTheHistoryRecordAndPreservesProjectAndOtherMissingEntries()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var service = new RecentProjectService(directory.Path);
        var project = Path.Combine(directory.Path, "keep-file.aeginext");
        var missing = Path.Combine(directory.Path, "keep-missing.aeginext");
        await File.WriteAllTextAsync(project, "project bytes");
        await service.RecordAsync(project);
        await service.RecordAsync(missing);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.RemoveAsync(Path.Combine(directory.Path, "nested", "..", "keep-file.aeginext"));
        await service.RemoveAsync(project);

        Assert.Equal(1, changes);
        Assert.Equal(missing, Assert.Single(service.Entries).Path);
        Assert.Equal("project bytes", await File.ReadAllTextAsync(project));
        await using var reopened = new RecentProjectService(directory.Path);
        Assert.Equal(service.Entries, reopened.Entries);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[{}]")]
    [InlineData("[{\"Path\":\"\",\"Name\":\"Invalid\",\"LastUsedUtc\":\"2026-10-06T00:00:00Z\"}]")]
    public async Task CorruptHistoryKeepsTheOriginalFileAndReportsADiagnostic(string json)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var historyPath = Path.Combine(directory.Path, "recent-projects.json");
        await File.WriteAllTextAsync(historyPath, json);

        await using var service = new RecentProjectService(directory.Path);

        Assert.Empty(service.Entries);
        Assert.NotNull(service.LastError);
        Assert.Equal(json, await File.ReadAllTextAsync(historyPath));
    }

    [Fact]
    public async Task UnreadableAndOversizedHistoryRemainAvailableForDiagnosis()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var historyPath = Path.Combine(directory.Path, "recent-projects.json");
        Directory.CreateDirectory(historyPath);
        await using (var inaccessible = new RecentProjectService(directory.Path))
        {
            Assert.Empty(inaccessible.Entries);
            Assert.NotNull(inaccessible.LastError);
            Assert.True(Directory.Exists(historyPath));
        }

        Directory.Delete(historyPath);
        await File.WriteAllTextAsync(historyPath, new string(' ', 65537));
        await using var oversized = new RecentProjectService(directory.Path);
        Assert.Empty(oversized.Entries);
        Assert.IsType<InvalidDataException>(oversized.LastError);
        Assert.Equal(65537, new FileInfo(historyPath).Length);
    }

    [Fact]
    public async Task WriteFailureKeepsInMemoryEntriesAndSignalsTheErrorWithoutFaultingTheOperation()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var service = new RecentProjectService(directory.Path);
        var historyPath = Path.Combine(directory.Path, "recent-projects.json");
        Directory.CreateDirectory(historyPath);
        var project = Path.Combine(directory.Path, "opened.aeginext");
        var errors = new List<Exception?>();
        service.ErrorChanged += (_, _) => errors.Add(service.LastError);

        await service.RecordAsync(project);

        Assert.Equal(project, Assert.Single(service.Entries).Path);
        Assert.NotNull(service.LastError);
        Assert.NotNull(Assert.Single(errors));
        Assert.True(service.Completion.IsCompletedSuccessfully);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
        Directory.Delete(historyPath);
        await service.RecordAsync(project);
        Assert.Null(service.LastError);
        Assert.Null(errors[1]);
        await using var reopened = new RecentProjectService(directory.Path);
        Assert.Equal(service.Entries, reopened.Entries);
    }
}
