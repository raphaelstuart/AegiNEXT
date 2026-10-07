using AegiNext.Application;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class StartupTestDialogService : IWorkbenchDialogService
{
    internal string? OpenPath { get; set; }
    internal string? SavePath { get; set; }
    internal TaskCompletionSource<string?>? PendingSelection { get; set; }
    internal TaskCompletionSource<string?>? PendingSaveSelection { get; set; }
    internal ProjectCreationRequest? NewProjectRequest { get; set; }
    internal TaskCompletionSource<ProjectCreationRequest?>? PendingNewProjectSelection { get; set; }
    internal int NewProjectCount { get; private set; }
    internal string? WorkspaceRoot { get; private set; }
    internal ProjectOpenResult? CreationResult { get; private set; }
    internal int OpenCount { get; private set; }
    internal int SaveCount { get; private set; }
    internal int UnsavedChoice { get; set; } = 2;
    internal int ConfirmationCount { get; private set; }
    internal string? SaveTitle { get; private set; }
    internal string? SaveExtension { get; private set; }
    internal string? SuggestedSaveName { get; private set; }
    internal bool UnavailableMediaChoice { get; set; }
    internal int UnavailableMediaCount { get; private set; }
    internal TaskCompletionSource<bool>? PendingMediaConfirmation { get; set; }

    internal string[]? OpenPaths { get; set; }
    internal string? FolderPath { get; set; }
    internal int FolderRequests { get; private set; }
    internal int PresetChoice { get; set; } = 2;
    internal TaskCompletionSource<int>? PendingPresetDecision { get; set; }
    internal int PresetConfirmationRequests { get; private set; }

    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns)
    {
        return Task.FromResult<IReadOnlyList<string>>(OpenPaths ?? (OpenPath is null ? [] : [OpenPath]));
    }

    public Task<string?> OpenFolderAsync(string title)
    {
        FolderRequests++;
        return Task.FromResult(FolderPath);
    }

    public Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<int?>(null);
    }

    public Task<int> ConfirmPresetChangesAsync(bool effects)
    {
        PresetConfirmationRequests++;
        return PendingPresetDecision?.Task ?? Task.FromResult(PresetChoice);
    }

    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        OpenCount++;
        return PendingSelection?.Task ?? Task.FromResult(OpenPath);
    }

    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName)
    {
        SaveCount++;
        SaveTitle = title;
        SaveExtension = extension;
        SuggestedSaveName = suggestedName;
        return PendingSaveSelection?.Task ?? Task.FromResult(SavePath);
    }

    public async Task<bool> ShowNewProjectAsync(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        CancellationToken cancellationToken = default)
    {
        NewProjectCount++;
        WorkspaceRoot = workspaceRoot;
        var request = PendingNewProjectSelection is { } pending
            ? await pending.Task.WaitAsync(cancellationToken) : NewProjectRequest;
        if (request is null)
        {
            return false;
        }
        CreationResult = await create(request, cancellationToken);
        return CreationResult.Status == ProjectOpenStatus.OPENED;
    }

    public Task<int> ConfirmUnsavedAsync()
    {
        ConfirmationCount++;
        return Task.FromResult(UnsavedChoice);
    }

    public Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnavailableMediaCount++;
        return PendingMediaConfirmation?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(UnavailableMediaChoice);
    }

    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
        => Task.FromResult(TrackStyleUpdateDecision.CANCEL);
}
