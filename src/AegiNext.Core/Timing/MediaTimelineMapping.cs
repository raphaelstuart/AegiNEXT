namespace AegiNext.Core.Timing;

/// <summary>
/// 将媒体原点平移到工程零点；默认值为恒等映射，不包含变速或剪辑语义。
/// </summary>
public readonly record struct MediaTimelineMapping
{
    /// <summary>
    /// 指定工程零点对应的媒体时间。
    /// </summary>
    public MediaTimelineMapping(MediaTime origin)
    {
        Origin = origin;
    }

    public MediaTime Origin { get; }

    /// <summary>
    /// 将媒体时间精确映射到工程时间。
    /// </summary>
    public MediaTime ToProjectTime(MediaTime mediaTime)
    {
        return mediaTime - Origin;
    }

    /// <summary>
    /// 将工程时间精确映射回媒体时间。
    /// </summary>
    public MediaTime ToMediaTime(MediaTime projectTime)
    {
        return projectTime + Origin;
    }
}
