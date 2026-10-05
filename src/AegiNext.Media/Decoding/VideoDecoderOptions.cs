namespace AegiNext.Media.Decoding;

/// <summary>不可变解码选项；自动模式仅允许在首帧交付前回退。</summary>
public sealed record VideoDecoderOptions
{
    public VideoDecodeMode Mode { get; init; } = VideoDecodeMode.Auto;

    public VideoDecodeWorkload Workload { get; init; } = VideoDecodeWorkload.Interactive;
}
