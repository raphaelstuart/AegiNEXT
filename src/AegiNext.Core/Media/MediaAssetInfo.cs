using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Media;

/// <summary>
/// 一次探测得到的容器与全部媒体流事实，不包含选轨决策或完整逐帧验证结论。
/// </summary>
public sealed record MediaAssetInfo
{
    public string? FormatName { get; init; }

    public MediaTime? ReportedStart { get; init; }

    public MediaTime? ReportedDuration { get; init; }

    public ImmutableDictionary<string, string> Tags { get; init; } = ImmutableDictionary<string, string>.Empty;

    public ImmutableArray<MediaStreamInfo> Streams { get; init; } = [];
}
