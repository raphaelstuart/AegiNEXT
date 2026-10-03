using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>退出操作的精确区间与下一会话；结束本句不会创建下一句。</summary>
public sealed record TimingExitResult(TimingSession Session, Guid CueId, MediaTime Start, MediaTime End);
