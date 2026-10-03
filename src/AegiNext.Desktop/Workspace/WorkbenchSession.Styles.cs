namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void NotifyStyleLibraryChanged() => StyleLibraryChanged?.Invoke(this, EventArgs.Empty);
    internal Task ApplySelectedStyleAsync()
    {
        return ViewModel.Styles.SelectedPreset is { } selected
            ? styles.ApplyAsync(styleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id))
            : Task.CompletedTask;
    }
}
