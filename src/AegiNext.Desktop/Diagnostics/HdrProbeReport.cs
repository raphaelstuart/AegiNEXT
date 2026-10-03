using AegiNext.Media;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class HdrProbeReport
{
    public string Scope { get; } = "macOS native HDR probe; GPU offscreen readback; no physical luminance certification";
    public bool Automatic { get; set; }
    public bool ResizeVerified { get; set; }
    public bool ReparentVerified { get; set; }
    public bool RecreationVerified { get; set; }
    public int CreatedContexts { get; set; }
    public uint? LiveContextsAfterClose { get; set; }
    public HdrPreviewStatus? Initial { get; set; }
    public HdrPreviewStatus? Resized { get; set; }
    public HdrPreviewStatus? Final { get; set; }
    public HdrVerificationResult? Verification { get; set; }
    public string? Failure { get; set; }
}
