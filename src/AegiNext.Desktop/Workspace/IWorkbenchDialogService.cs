namespace AegiNext.Desktop.Workspace;

internal interface IWorkbenchDialogService
{
    Task<string?> OpenFileAsync(string title, string typeName, string[] patterns);
    Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName);
    Task<int> ConfirmUnsavedAsync();
    Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount);
}
