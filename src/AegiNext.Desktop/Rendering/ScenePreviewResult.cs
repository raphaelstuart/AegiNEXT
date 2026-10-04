using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record ScenePreviewResult(ScenePreviewRequest Request, SdrVideoFrame? Frame, Exception? Error,
    double ComposeMilliseconds);
