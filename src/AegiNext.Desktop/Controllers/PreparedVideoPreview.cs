using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controllers;

internal sealed record PreparedVideoPreview(VideoPreviewDelivery Identity, SdrVideoFrame Frame, long Bytes)
{
    internal TaskCompletionSource Submitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
