using AegiNext.Core.Media;

namespace AegiNext.Media.Probing;

/// <summary>
/// 本地媒体的流级探测快照；不表示已扫描所有帧或验证了 HDR 成片。
/// </summary>
public sealed record MediaProbeReport(string SourcePath, MediaAssetInfo Asset,
    FfprobeToolIdentity Tool, string Diagnostics);
