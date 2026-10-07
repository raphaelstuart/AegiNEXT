using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class TransferSettingsDialogStub : IWorkbenchDialogService
{
    internal StartupTestDialogService Inner { get; } = new();
    internal bool RestartChoice { get; set; }
    internal int RestartRequests { get; private set; }
    internal TaskCompletionSource<bool>? PendingRestart { get; set; }
    internal TaskCompletionSource RestartShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task<bool> ConfirmSettingsRestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestartRequests++;
        RestartShown.TrySetResult();
        return PendingRestart?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(RestartChoice);
    }

    /// <inheritdoc />
    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        return Inner.OpenFileAsync(title, typeName, patterns);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns)
    {
        return Inner.OpenFilesAsync(title, typeName, patterns);
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
    public Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
    {
        return Inner.ShowIntegerInputAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName)
    {
        return Inner.SaveFileAsync(title, typeName, patterns, extension, suggestedName);
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
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        return Inner.ConfirmTrackStyleChangeAsync(trackName, presetName, subtitleCount);
    }
}
