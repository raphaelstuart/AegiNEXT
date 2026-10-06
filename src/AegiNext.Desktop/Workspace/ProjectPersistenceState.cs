using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record ProjectPersistenceState(long Generation, string ProjectPath, string ProjectDirectory,
    ProjectDocument Snapshot, bool HasUnsavedChanges, bool IsBusy)
{
    internal ProjectDocument ContentSnapshot { get; init; } = Snapshot;
}
