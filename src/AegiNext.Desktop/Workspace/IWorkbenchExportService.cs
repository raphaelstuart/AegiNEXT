using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal interface IWorkbenchExportService : IDisposable
{
    Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken);
}
