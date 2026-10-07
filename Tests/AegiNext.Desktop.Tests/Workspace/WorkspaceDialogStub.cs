using AegiNext.Desktop.Workspace;
using AegiNext.Application;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class WorkspaceDialogStub : IWorkbenchDialogService
{
    internal string? OpenPath { get; set; }
    internal string? SavePath { get; set; }
    internal ProjectCreationRequest? NewProjectRequest { get; set; }
    internal ProjectOpenResult? LastCreationResult { get; private set; }
    internal string? SuggestedWorkspaceRoot { get; private set; }
    internal int NewProjectRequests { get; private set; }
    internal TaskCompletionSource NewProjectShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<ProjectCreationRequest?>? PendingNewProjectRequest { get; set; }

    public async Task<bool> ShowNewProjectAsync(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        CancellationToken cancellationToken = default)
    {
        SuggestedWorkspaceRoot = workspaceRoot;
        NewProjectRequests++;
        NewProjectShown.TrySetResult();
        var selection = PendingNewProjectRequest is { } pending
            ? await pending.Task.WaitAsync(cancellationToken) : NewProjectRequest;
        if (selection is not { } request)
        {
            return false;
        }
        LastCreationResult = await create(request, cancellationToken);
        return LastCreationResult.Status == ProjectOpenStatus.OPENED;
    }
    internal int UnsavedChoice { get; set; }
    internal int SaveRequests { get; private set; }
    internal string? SuggestedSaveName { get; private set; }
    internal int ConfirmationRequests { get; private set; }
    internal bool ConversionChoice { get; set; }
    internal IReadOnlyList<string> ConversionDiagnostics { get; private set; } = [];
    internal TrackStyleUpdateDecision TrackStyleChoice { get; set; } = TrackStyleUpdateDecision.DEFAULT_ONLY;
    internal int TrackStyleRequests { get; private set; }
    internal bool TrackDeletionChoice { get; set; }
    internal int TrackDeletionRequests { get; private set; }
    internal string? DeletedTrackName { get; private set; }
    internal int DeletedTrackSubtitleCount { get; private set; }
    internal TaskCompletionSource<bool>? PendingTrackDeletion { get; set; }
    internal bool UnavailableMediaChoice { get; set; }
    internal int UnavailableMediaRequests { get; private set; }
    internal string? UnavailableMediaPath { get; private set; }
    internal string? UnavailableMediaReason { get; private set; }
    internal TaskCompletionSource<bool>? PendingMediaConfirmation { get; set; }

    internal string[]? OpenPaths { get; set; }
    internal int OpenFilesRequests { get; private set; }
    internal TaskCompletionSource OpenFilesShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<IReadOnlyList<string>>? PendingOpenFiles { get; set; }
    internal string? FolderPath { get; set; }
    internal int FolderRequests { get; private set; }
    internal int PresetChoice { get; set; } = 2;
    internal int PresetConfirmationRequests { get; private set; }

    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns)
    {
        OpenFilesRequests++;
        OpenFilesShown.TrySetResult();
        return PendingOpenFiles?.Task ?? Task.FromResult<IReadOnlyList<string>>(OpenPaths ?? (OpenPath is null ? [] : [OpenPath]));
    }

    public Task<string?> OpenFolderAsync(string title)
    {
        FolderRequests++;
        return Task.FromResult(FolderPath);
    }

    public Task<int> ConfirmPresetChangesAsync(bool effects)
    {
        PresetConfirmationRequests++;
        return Task.FromResult(PresetChoice);
    }

    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        return Task.FromResult(OpenPath);
    }

    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension,
        string suggestedName)
    {
        SaveRequests++;
        SuggestedSaveName = suggestedName;
        return Task.FromResult(SavePath);
    }

    public Task<bool> ConfirmSubtitleConversionAsync(IReadOnlyList<string> diagnostics)
    {
        ConversionDiagnostics = diagnostics;
        return Task.FromResult(ConversionChoice);
    }

    public Task<int> ConfirmUnsavedAsync()
    {
        ConfirmationRequests++;
        return Task.FromResult(UnsavedChoice);
    }

    public Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        UnavailableMediaRequests++;
        UnavailableMediaPath = mediaPath;
        UnavailableMediaReason = reason;
        return PendingMediaConfirmation?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(UnavailableMediaChoice);
    }

    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        TrackStyleRequests++;
        return Task.FromResult(TrackStyleChoice);
    }

    public Task<bool> ConfirmTrackDeletionAsync(string trackName, int subtitleCount, CancellationToken cancellationToken)
    {
        TrackDeletionRequests++;
        DeletedTrackName = trackName;
        DeletedTrackSubtitleCount = subtitleCount;
        return PendingTrackDeletion?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(TrackDeletionChoice);
    }
}
