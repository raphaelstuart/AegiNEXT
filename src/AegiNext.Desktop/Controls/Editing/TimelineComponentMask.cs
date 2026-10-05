namespace AegiNext.Desktop.Controls;

/// <summary>一次关键帧操作涉及的分量；位序与动画值分量顺序一致。</summary>
[Flags]
public enum TimelineComponentMask
{
    NONE = 0,
    FIRST = 1,
    SECOND = 2,
    THIRD = 4,
    FOURTH = 8
}
