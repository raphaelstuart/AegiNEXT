namespace AegiNext.Core.Media;

/// <summary>
/// 音频流的采样率、声道数与原始声道布局名称；缺失布局不由声道数推断。
/// </summary>
public sealed record MediaAudioInfo
{
    public int? SampleRate { get; init; }

    public int? Channels { get; init; }

    public string? ChannelLayout { get; init; }
}
