using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectPersistenceStorage : IProjectPersistenceStorage
{
    public Task SaveAsync(ProjectDocument snapshot, string path, CancellationToken cancellationToken)
    {
        var context = AegiTaskExecutionContext.Current;
        return context is null ? ProjectStore.SaveAsync(snapshot, path, cancellationToken)
            : ProjectStore.SaveAsync(snapshot, path, () => context.EnterCommit(), cancellationToken);
    }

    public async Task WriteBackupAsync(ProjectDocument snapshot, string projectPath, DateTimeOffset timestamp,
        int maximumCount, CancellationToken cancellationToken)
    {
        var context = AegiTaskExecutionContext.Current;
        var pending = context is null
            ? ProjectBackupStore.WriteAsync(snapshot, projectPath, timestamp, maximumCount, cancellationToken)
            : ProjectBackupStore.WriteAsync(snapshot, projectPath, timestamp, maximumCount,
                () => context.EnterCommit(), cancellationToken);
        await pending
            .ConfigureAwait(false);
    }

    public Task PruneBackupsAsync(string projectPath, int maximumCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AegiTaskExecutionContext.Current?.EnterCommit();
        return ProjectBackupStore.PruneAsync(projectPath, maximumCount, CancellationToken.None);
    }
}
