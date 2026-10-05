namespace AegiNext.Desktop.Controllers;

internal sealed class VideoPreviewOpenRequest(long expectedEpoch)
{
    internal long ExpectedEpoch { get; } = expectedEpoch;
    internal long? AllocatedEpoch { get; set; }
}
