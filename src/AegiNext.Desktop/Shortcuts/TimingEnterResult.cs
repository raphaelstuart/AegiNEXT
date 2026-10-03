using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>进入操作的编辑计划；IsRepeated 时调用方无需再次修改字幕。</summary>
public sealed record TimingEnterResult(TimingSession Session, Guid CueId, MediaTime Start, bool IsNew, bool IsRepeated);
