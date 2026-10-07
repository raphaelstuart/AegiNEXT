using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>用户通过水平或垂直按钮组确认的字幕对齐。</summary>
public sealed class SubtitleAlignmentChangedEventArgs(TextAlignment alignment) : EventArgs
{
    /// <summary>两个方向合并后的九宫格对齐。</summary>
    public TextAlignment Alignment { get; } = alignment;
}
