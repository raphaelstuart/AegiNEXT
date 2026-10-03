namespace AegiNext.Core.Media;

/// <summary>
/// 单个 content light 元数据条目，亮度单位为 cd/m²；零表示未指定，来源由流或帧容器表达。
/// </summary>
public sealed record MediaContentLightInfo
{
    public uint? MaxContentLightLevel { get; init; }

    public uint? MaxFrameAverageLightLevel { get; init; }
}
