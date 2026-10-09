using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class ExportSettingsDialogStub : IWorkbenchDialogService
{
    internal StartupTestDialogService Inner { get; } = new();
    internal TaskCompletionSource<IReadOnlyList<string>>? PendingInput { get; set; }
    internal TaskCompletionSource<string?>? PendingOutput { get; set; }
    internal int InputRequests { get; private set; }
    internal TaskCompletionSource InputShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource OutputShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        return Inner.OpenFileAsync(title, typeName, patterns);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns)
    {
        InputRequests++;
        var result = Inner.OpenFilesAsync(title, typeName, patterns);
        InputShown.TrySetResult();
        return PendingInput?.Task ?? result;
    }

    /// <inheritdoc />
    public Task<string?> OpenFolderAsync(string title)
    {
        return Inner.OpenFolderAsync(title);
    }

    /// <inheritdoc />
    public Task<int> ConfirmPresetChangesAsync(bool effects)
    {
        return Inner.ConfirmPresetChangesAsync(effects);
    }

    /// <inheritdoc />
    public Task<bool> ConfirmPresetDeletionAsync(PresetDeletionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
    {
        return Inner.ShowIntegerInputAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension,
        string suggestedName, string? suggestedDirectory = null)
    {
        var result = Inner.SaveFileAsync(title, typeName, patterns, extension, suggestedName, suggestedDirectory);
        OutputShown.TrySetResult();
        return PendingOutput?.Task ?? result;
    }

    /// <inheritdoc />
    public Task ShowErrorAsync(string titleKey, string message, CancellationToken cancellationToken = default)
    {
        return Inner.ShowErrorAsync(titleKey, message, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ConfirmUnsavedAsync()
    {
        return Inner.ConfirmUnsavedAsync();
    }

    /// <inheritdoc />
    public Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        return Inner.ConfirmUnavailableMediaAsync(mediaPath, reason, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName,
        int subtitleCount)
    {
        return Inner.ConfirmTrackStyleChangeAsync(trackName, presetName, subtitleCount);
    }
}
