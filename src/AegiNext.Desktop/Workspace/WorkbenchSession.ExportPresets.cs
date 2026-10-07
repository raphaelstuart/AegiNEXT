namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void ApplyExportPreset(Guid id)
    {
        exportPresets.Apply(id);
    }

    private void OnApplicationExportPresetsChanged(object? sender, EventArgs e)
    {
        exportPresets.Refresh();
    }

    private void OnApplicationLibrariesBusyChanged(object? sender, EventArgs e)
    {
        ViewModel.Export.RefreshPresetCommands();
    }
}
