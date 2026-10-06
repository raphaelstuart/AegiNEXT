using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal interface IProjectPersistenceStorage
{
    Task SaveAsync(ProjectDocument snapshot, string path, CancellationToken cancellationToken);
    Task WriteBackupAsync(ProjectDocument snapshot, string projectPath, DateTimeOffset timestamp, int maximumCount,
        CancellationToken cancellationToken);
    Task PruneBackupsAsync(string projectPath, int maximumCount, CancellationToken cancellationToken);
}
