using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record SubtitleStylePreviewResult(SubtitleStylePreviewRequest Request, SdrVideoFrame? Frame, Exception? Error);
