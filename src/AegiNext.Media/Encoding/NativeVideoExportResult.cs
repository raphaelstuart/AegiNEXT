using AegiNext.Media.Decoding;

namespace AegiNext.Media.Encoding;

internal sealed record NativeVideoExportResult(ulong Frames, string Encoder,
    VideoDecodeSessionInfo Decoder, VideoExportColor OutputColor);
