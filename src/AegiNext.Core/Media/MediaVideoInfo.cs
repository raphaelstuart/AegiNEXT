using System.Collections.Immutable;

namespace AegiNext.Core.Media;

/// <summary>
/// 视频流级探测信息；帧率不是逐帧时间戳，流级 HDR 元数据不是全片覆盖证明。
/// </summary>
public sealed record MediaVideoInfo
{
    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? PixelFormat { get; init; }

    public int? BitsPerRawSample { get; init; }

    public MediaRatio? SampleAspectRatio { get; init; }

    public MediaRatio? FrameRate { get; init; }

    public MediaRatio? AverageFrameRate { get; init; }

    public MediaColorInfo Color { get; init; } = new();

    public ImmutableArray<MediaMasteringDisplayInfo> MasteringDisplays { get; init; } = [];

    public ImmutableArray<MediaContentLightInfo> ContentLightLevels { get; init; } = [];

    public ImmutableArray<MediaDisplayMatrixInfo> DisplayMatrices { get; init; } = [];

    public ImmutableArray<string> SideDataTypes { get; init; } = [];
}
