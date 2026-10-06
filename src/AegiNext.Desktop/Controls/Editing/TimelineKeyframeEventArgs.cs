using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>效果轴上的关键帧选择或移动，时间相对层起点。</summary>
public sealed class TimelineKeyframeEventArgs : EventArgs
{
    /// <summary>创建关键帧操作。</summary>
    public TimelineKeyframeEventArgs(Guid layerId, AnimationTrackTarget target, MediaTime oldTime, MediaTime newTime,
        AnimationValue? newValue = null, TimelineComponentMask components = TimelineComponentMask.FIRST)
    {
        LayerId = layerId;
        Target = target;
        OldTime = oldTime;
        NewTime = newTime;
        NewValue = newValue;
        Components = components;
    }

    /// <summary>创建普通属性的关键帧操作。</summary>
    public TimelineKeyframeEventArgs(Guid layerId, AnimationProperty property, MediaTime oldTime, MediaTime newTime,
        AnimationValue? newValue = null, TimelineComponentMask components = TimelineComponentMask.FIRST)
        : this(layerId, new AnimationTrackTarget(property), oldTime, newTime, newValue, components)
    {
    }

    public Guid LayerId { get; }
    public Guid? OperationId { get; init; }
    public bool IsOperationStart { get; init; }
    public AnimationTrackTarget Target { get; }
    public AnimationProperty Property => Target.Property;
    public MediaTime OldTime { get; }
    public MediaTime NewTime { get; }
    public AnimationValue? NewValue { get; }
    public TimelineComponentMask Components { get; }

    /// <summary>选择接收方完成草稿验证后允许控件开始手势。</summary>
    public bool SelectionAccepted { get; set; }
}
