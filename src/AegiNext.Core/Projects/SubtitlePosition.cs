namespace AegiNext.Core.Projects;

/// <summary>以画布归一化锚点、字形包围盒归一化轴心和像素偏移定位字幕；不拉伸文字。</summary>
public sealed record SubtitlePosition
{
    public ScenePoint Anchor { get; init; } = new(0.5, 1);
    public ScenePoint Pivot { get; init; } = new(0.5, 1);
    public ScenePoint Offset { get; init; } = new(0, -40);

    /// <summary>将九宫格对齐与边距转为可编辑的锚点；旧版字形基线补偿由实际排版测量提供。</summary>
    public static SubtitlePosition FromAlignment(TextAlignment alignment, double margin)
    {
        var horizontal = (int)alignment % 3;
        var vertical = (int)alignment / 3;
        var anchor = new ScenePoint(horizontal / 2.0, vertical / 2.0);
        return new()
        {
            Anchor = anchor,
            Pivot = anchor,
            Offset = new(horizontal == 0 ? margin : horizontal == 2 ? -margin : 0,
                vertical == 0 ? margin : vertical == 2 ? -margin : 0)
        };
    }
}
