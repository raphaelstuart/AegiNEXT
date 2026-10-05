using AegiNext.Media.Decoding;

namespace AegiNext.Media.Encoding;

internal sealed record ExportWorkerMessage(string Type, ulong Frames = 0, long PositionNumerator = 0,
    long PositionDenominator = 1, string? Error = null, string? Encoder = null,
    VideoDecodeSessionInfo? Decoder = null, VideoExportColor? OutputColor = null);
