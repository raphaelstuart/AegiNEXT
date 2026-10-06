using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>应用层提供的新字幕临时时间范围，不修改工程快照。</summary>
public sealed record TimelineTimingPreview(Guid CueId, MediaTime Start, MediaTime End);
