using AegiNext.Application;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly ProjectPersistenceCoordinator persistence;
    private readonly CancellationTokenSource projectOperationsCancellation = new();
    private long projectGeneration;

    internal ProjectPersistenceCoordinator Persistence => persistence;
    internal CancellationToken ProjectOperationsToken => projectOperationsCancellation.Token;

    internal void ActivateProjectPersistence()
    {
        projectGeneration++;
        persistence.ActivateProject();
    }

    private ProjectPersistenceState? CapturePersistenceState()
    {
        if (projectPath is null || closing)
        {
            return null;
        }

        var contentSnapshot = editor.Snapshot;
        return new(projectGeneration, projectPath, projectDirectory,
            CreatePersistenceSnapshot(contentSnapshot), HasUnsavedChanges, projectBusy || !documentChangeTask.IsCompleted)
        {
            ContentSnapshot = contentSnapshot
        };
    }

    private void OnProjectAutomaticallySaved(ProjectPersistenceSaveResult result)
    {
        if (closing || result.State.Generation != projectGeneration || !PathsEqual(projectPath, result.State.ProjectPath))
        {
            return;
        }

        AcceptProjectSave(result.State.ContentSnapshot, result.PersistedSnapshot);
        LogInfo("Project", I18n.Localization.Get("WorkflowLog.ProjectAutoSaved"), result.State.ProjectPath);
    }

    private void OnEditorStateChanged(object? sender, ProjectEditorChangedEventArgs args)
    {
        if (args.Kind == ProjectEditorChangeKind.SAVE_POINT)
        {
            RefreshTitle();
            ViewModel.RefreshCommands();
            return;
        }

        OnDocumentChanged(sender, args);
    }

    internal static bool PathsEqual(string? left, string? right) => string.Equals(left, right,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
