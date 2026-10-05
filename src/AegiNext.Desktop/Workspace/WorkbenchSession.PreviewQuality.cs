using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private long previewQualityRevision;

    private ProjectPreviewState CreatePreviewState() => new(DocumentSnapshot, ProjectDirectory, ProjectPosition,
        playback.IsInteractive, preferences.PreviewQuality, previewQualityRevision);
}
