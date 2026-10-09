using AegiNext.Application;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class PresetDeletionDialogStub : IWorkbenchDialogService
{
    private readonly StartupTestDialogService inner = new();

    internal TaskCompletionSource<bool> PendingDeletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<PresetDeletionRequest> DeletionShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken DeletionToken { get; private set; }
    internal int DeletionRequests { get; private set; }

    public Task<bool> ConfirmPresetDeletionAsync(PresetDeletionRequest request, CancellationToken cancellationToken = default)
    {
        DeletionRequests++;
        DeletionToken = cancellationToken;
        DeletionShown.TrySetResult(request);
        return PendingDeletion.Task;
    }

    public Task<int> ConfirmPresetChangesAsync(bool effects) => Task.FromResult(1);
    public Task<int> ConfirmExportPresetChangesAsync() => Task.FromResult(1);
    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns) => inner.OpenFileAsync(title, typeName, patterns);
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns) => inner.OpenFilesAsync(title, typeName, patterns);
    public Task<string?> OpenFolderAsync(string title) => inner.OpenFolderAsync(title);
    public Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
        => inner.ShowIntegerInputAsync(request, cancellationToken);
    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName,
        string? suggestedDirectory = null)
        => inner.SaveFileAsync(title, typeName, patterns, extension, suggestedName, suggestedDirectory);
    /// <inheritdoc />
    public Task ShowErrorAsync(string titleKey, string message, CancellationToken cancellationToken = default)
        => inner.ShowErrorAsync(titleKey, message, cancellationToken);
    public Task<int> ConfirmUnsavedAsync() => inner.ConfirmUnsavedAsync();
    public Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
        => inner.ConfirmUnavailableMediaAsync(mediaPath, reason, cancellationToken);
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
        => inner.ConfirmTrackStyleChangeAsync(trackName, presetName, subtitleCount);
}
