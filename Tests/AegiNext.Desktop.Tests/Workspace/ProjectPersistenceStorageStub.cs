using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class ProjectPersistenceStorageStub : IProjectPersistenceStorage
{
    internal List<ProjectDocument> SavedSnapshots { get; } = [];
    internal List<ProjectDocument> BackupSnapshots { get; } = [];
    internal List<int> PrunedLimits { get; } = [];
    internal Func<CancellationToken, Task>? SaveWork { get; set; }
    internal Func<CancellationToken, Task>? BackupWork { get; set; }
    internal Func<CancellationToken, Task>? PruneWork { get; set; }
    internal int SaveAttempts { get; private set; }
    internal int BackupAttempts { get; private set; }
    internal List<DateTimeOffset> BackupTimes { get; } = [];

    public async Task SaveAsync(ProjectDocument snapshot, string path, CancellationToken cancellationToken)
    {
        SaveAttempts++;
        if (SaveWork is { } work)
        {
            await work(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SavedSnapshots.Add(snapshot);
    }

    public async Task WriteBackupAsync(ProjectDocument snapshot, string projectPath, DateTimeOffset timestamp,
        int maximumCount, CancellationToken cancellationToken)
    {
        BackupAttempts++;
        BackupSnapshots.Add(snapshot);
        BackupTimes.Add(timestamp);
        if (BackupWork is { } work)
        {
            await work(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task PruneBackupsAsync(string projectPath, int maximumCount, CancellationToken cancellationToken)
    {
        PrunedLimits.Add(maximumCount);
        if (PruneWork is { } work)
        {
            await work(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
