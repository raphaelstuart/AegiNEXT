namespace AegiNext.Media.Decoding;

/// <summary>解码会话的实际后端、回退原因和累积阶段耗时快照。</summary>
public sealed record VideoDecodeSessionInfo(VideoDecodeMode RequestedMode, VideoDecoderBackend ActiveBackend,
    bool HardwareConfirmed, string FallbackReason, ulong Generation, ulong DeliveredFrames,
    ulong DecodeNanoseconds = 0, ulong DownloadNanoseconds = 0);
