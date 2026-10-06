using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>时间线主体右键命中的稳定片段、轨道和工程时间。</summary>
public sealed class TimelineClipContextEventArgs(Guid? trackId, Guid? layerId, MediaTime time) : EventArgs
{
    public Guid? TrackId { get; } = trackId;
    public Guid? LayerId { get; } = layerId;
    public MediaTime Time { get; } = time;
}
