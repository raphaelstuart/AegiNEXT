using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportPresetCoordinator(WorkbenchSession session)
{
    internal void Refresh()
    {
        if (session.IsClosing)
        {
            return;
        }

        session.ViewModel.Export.RefreshPresets(session.ApplicationContext.ExportPresetLibrary.Snapshot.Presets);
        session.ViewModel.Export.RefreshPresetCommands();
    }

    internal void Apply(Guid id)
    {
        if (session.IsClosing)
        {
            return;
        }

        var preset = session.ApplicationContext.ExportPresetLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == id)
            ?? throw new InvalidDataException(Localization.Get("Workbench.ExportPresetMissing"));
        session.ViewModel.Export.ApplySettings(preset.Settings);
    }
}
