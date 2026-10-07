using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>请求为命中的动画属性打开菜单；片段身份存在时限定该片段，否则定位所属整行。</summary>
public sealed class TimelineAnimationRowContextEventArgs(TimelineAnimationRowId id, Guid? clipId = null) : EventArgs
{
    public TimelineAnimationRowId Id { get; } = id;
    public Guid? ClipId { get; } = clipId;
}
