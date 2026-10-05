using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private long previewQualityRevision;

    internal ProjectPreviewState GetPreviewState() => Volatile.Read(ref previewState);

    private ProjectPreviewState CreatePreviewState() => new(PreviewDocument, ProjectDirectory, ProjectPosition,
        playback.IsInteractive, preferences.PreviewQuality, previewQualityRevision);
}
