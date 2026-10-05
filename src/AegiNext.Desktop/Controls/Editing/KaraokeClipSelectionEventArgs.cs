namespace AegiNext.Desktop.Controls;

/// <summary>卡拉 OK 轴上的稳定片段选中请求。</summary>
public sealed class KaraokeClipSelectionEventArgs(Guid clipId) : EventArgs
{
    public Guid ClipId { get; } = clipId;
}
