using AegiNext.Application;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectPersistenceStorage : IProjectPersistenceStorage
{
    public Task SaveAsync(ProjectDocument snapshot, string path, CancellationToken cancellationToken)
    {
        return ProjectStore.SaveAsync(snapshot, path, cancellationToken);
    }

    public async Task WriteBackupAsync(ProjectDocument snapshot, string projectPath, DateTimeOffset timestamp,
        int maximumCount, CancellationToken cancellationToken)
    {
        await ProjectBackupStore.WriteAsync(snapshot, projectPath, timestamp, maximumCount, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task PruneBackupsAsync(string projectPath, int maximumCount, CancellationToken cancellationToken)
    {
        return ProjectBackupStore.PruneAsync(projectPath, maximumCount, cancellationToken);
    }
}
