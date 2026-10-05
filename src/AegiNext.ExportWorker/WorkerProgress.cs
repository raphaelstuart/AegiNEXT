using AegiNext.Media.Encoding;

namespace AegiNext.ExportWorker;

internal sealed class WorkerProgress : IProgress<VideoExportProgress>
{
    public void Report(VideoExportProgress value)
    {
        Program.Send(new("progress", value.FrameCount, value.Position.Numerator, value.Position.Denominator, Encoder: value.Encoder));
    }
}
