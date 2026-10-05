using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>一次卡拉 OK 拖拽释放后的语义时长，不包含中间工程修改。</summary>
public sealed class KaraokeClipDurationEventArgs(Guid subtitleId, Guid clipId, MediaTime duration) : EventArgs
{
    public Guid SubtitleId { get; } = subtitleId;
    public Guid ClipId { get; } = clipId;
    public MediaTime Duration { get; } = duration;
}
