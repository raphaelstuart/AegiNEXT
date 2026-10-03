using AegiNext.Application;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal static class WorkbenchCompositionRoot
{
    internal static WorkbenchSession Create(IWorkbenchDialogService dialogs,
        Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory = null,
        IWorkbenchExportService? exportService = null)
    {
        var editor = new ProjectEditor();
        var preferences = new WorkbenchPreferencesStore(Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY"));
        return new(dialogs, controllerFactory, editor: editor, preferencesStore: preferences,
            exportService: exportService ?? new VideoWorkbenchExportService(new VideoExporter()));
    }
}
