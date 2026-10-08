using AegiNext.Core.Projects;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class ProjectPersistenceTestContext : IAsyncDisposable
{
    private readonly TemporaryWorkbenchDirectory directory = new();

    internal ProjectPersistenceTestContext(Func<Action, Task>? dispatch = null)
    {
        State = new(1, Path.Combine(directory.Path, "project.aeginext"), directory.Path,
            new() { Name = "Committed" }, true, false);
        Preferences = new() { WorkspaceRoot = directory.Path };
        Tasks.RegisterScope(TaskScope, "Persistence test");
        Coordinator = new(Clock, dispatch ?? DispatchImmediately, () => State, OnSaved,
            error => Errors.Add(error), Storage, Tasks, TaskScope);
        Coordinator.UpdatePreferences(Preferences);
        Coordinator.ActivateProject();
    }

    internal ProjectPersistenceState? State { get; set; }
    internal ProjectPreferences Preferences { get; set; }
    internal ManualPlaybackTimeProvider Clock { get; } = new();
    internal ProjectPersistenceStorageStub Storage { get; } = new();
    internal ProjectPersistenceCoordinator Coordinator { get; }
    internal AegiTaskService Tasks { get; } = new();
    internal string TaskScope { get; } = Guid.NewGuid().ToString("N");
    internal List<ProjectPersistenceSaveResult> SaveResults { get; } = [];
    internal List<Exception> Errors { get; } = [];

    internal async Task AdvanceAsync(TimeSpan elapsed)
    {
        Clock.Advance(elapsed);
        await Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        await Coordinator.DisposeAsync();
        await Tasks.DisposeAsync();
        directory.Dispose();
    }

    private void OnSaved(ProjectPersistenceSaveResult result)
    {
        SaveResults.Add(result);
        if (State is { } current && current.Generation == result.State.Generation &&
            current.ProjectPath == result.State.ProjectPath && ReferenceEquals(current.Snapshot, result.State.Snapshot))
        {
            State = current with { Snapshot = result.PersistedSnapshot, HasUnsavedChanges = false };
        }
    }

    private static Task DispatchImmediately(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
