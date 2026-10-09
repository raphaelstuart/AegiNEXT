using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>完成轨道头拖拽后的一次重排请求，保留原始不可变工程身份。</summary>
public sealed class TimelineTrackReorderEventArgs(Guid trackId, int index, ProjectDocument expectedDocument) : EventArgs
{
    public Guid TrackId { get; } = trackId;
    public int Index { get; } = index;
    public ProjectDocument ExpectedDocument { get; } = expectedDocument;
}
