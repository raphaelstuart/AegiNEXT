using Avalonia;

namespace AegiNext.Desktop.Controls;

/// <summary>提供稳定片段身份和轴内锚定矩形，弹层与事务由宿主管理。</summary>
public sealed class KaraokeClipEditRequestedEventArgs(Guid subtitleId, Guid clipId, Rect anchor) : EventArgs
{
    public Guid SubtitleId { get; } = subtitleId;
    public Guid ClipId { get; } = clipId;
    public Rect Anchor { get; } = anchor;
}
