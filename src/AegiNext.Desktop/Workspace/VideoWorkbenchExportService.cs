using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal sealed class VideoWorkbenchExportService(VideoExporter exporter) : IWorkbenchExportService
{
    /// <summary>通过独立导出 worker 压制不可变的工作台工程快照。</summary>
    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        return exporter.ExportAsync(request, progress, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
