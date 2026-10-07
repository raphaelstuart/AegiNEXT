namespace AegiNext.Desktop.Controls;

/// <summary>请求按时间线头部命中的稳定字幕轨道身份切换 Solo 显示。</summary>
public sealed class TimelineTrackSoloEventArgs(Guid trackId) : EventArgs
{
    public Guid TrackId { get; } = trackId;
}
