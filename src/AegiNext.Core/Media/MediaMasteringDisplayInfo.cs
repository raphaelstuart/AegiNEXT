namespace AegiNext.Core.Media;

/// <summary>
/// 单个 mastering display 元数据条目，色度与 cd/m² 亮度保留精确比值；来源由流或帧容器表达。
/// </summary>
public sealed record MediaMasteringDisplayInfo
{
    public MediaRatio? RedX { get; init; }

    public MediaRatio? RedY { get; init; }

    public MediaRatio? GreenX { get; init; }

    public MediaRatio? GreenY { get; init; }

    public MediaRatio? BlueX { get; init; }

    public MediaRatio? BlueY { get; init; }

    public MediaRatio? WhitePointX { get; init; }

    public MediaRatio? WhitePointY { get; init; }

    public MediaRatio? MinLuminance { get; init; }

    public MediaRatio? MaxLuminance { get; init; }
}
