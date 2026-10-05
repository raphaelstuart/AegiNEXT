using AegiNext.Media.Decoding;

namespace AegiNext.Media.Encoding;

/// <summary>已原子提交的成片路径与帧数。</summary>
public sealed record VideoExportResult(string OutputPath, ulong Frames, string? Encoder = null,
    VideoDecodeSessionInfo? Decoder = null, VideoExportColor? OutputColor = null);
