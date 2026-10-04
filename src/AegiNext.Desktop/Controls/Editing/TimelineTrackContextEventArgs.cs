namespace AegiNext.Desktop.Controls;

/// <summary>请求在已命中的字幕轨道上打开管理菜单。</summary>
public sealed class TimelineTrackContextEventArgs(Guid? trackId) : EventArgs
{
    public Guid? TrackId { get; } = trackId;
}
