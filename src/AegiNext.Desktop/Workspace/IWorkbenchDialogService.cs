using AegiNext.Application;

namespace AegiNext.Desktop.Workspace;

internal interface IWorkbenchDialogService
{
    Task<string?> OpenFileAsync(string title, string typeName, string[] patterns);
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns);
    Task<string?> OpenFolderAsync(string title);
    Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default);
    Task<int> ConfirmPresetChangesAsync(bool effects);
    Task<int> ConfirmExportPresetChangesAsync() => ConfirmPresetChangesAsync(false);
    Task<bool> ConfirmSettingsRestartAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName);
    Task<bool> ShowNewProjectAsync(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<int> ConfirmUnsavedAsync();
    Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken);
    Task<bool> ConfirmSubtitleConversionAsync(IReadOnlyList<string> diagnostics) => Task.FromResult(false);
    Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount);
    Task<bool> ConfirmTrackDeletionAsync(string trackName, int subtitleCount, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
