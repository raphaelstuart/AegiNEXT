using System.Collections.Immutable;

namespace AegiNext.Core.Media;

/// <summary>
/// 保留来源流索引、原始类型名称和探测事实；未知流类型仍可存在于结果中。
/// </summary>
public sealed record MediaStreamInfo
{
    public required int Index { get; init; }

    public string? CodecType { get; init; }

    public string? CodecName { get; init; }

    public ImmutableDictionary<string, string> Tags { get; init; } = ImmutableDictionary<string, string>.Empty;

    public ImmutableDictionary<string, int> Disposition { get; init; } = ImmutableDictionary<string, int>.Empty;

    public MediaStreamTiming Timing { get; init; } = new();

    public MediaVideoInfo? Video { get; init; }

    public MediaAudioInfo? Audio { get; init; }
}
