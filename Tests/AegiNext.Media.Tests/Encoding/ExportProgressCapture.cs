using AegiNext.Media.Encoding;

namespace AegiNext.Media.Tests.Encoding;

internal sealed class ExportProgressCapture(Action<VideoExportProgress>? callback = null) : IProgress<VideoExportProgress>
{
    internal List<VideoExportProgress> Values { get; } = [];

    public void Report(VideoExportProgress value)
    {
        Values.Add(value);
        callback?.Invoke(value);
    }
}
