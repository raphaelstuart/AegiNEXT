using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>效果轴上的关键帧选择或移动，时间相对层起点。</summary>
public sealed class TimelineKeyframeEventArgs : EventArgs
{
    /// <summary>创建关键帧操作。</summary>
    public TimelineKeyframeEventArgs(Guid layerId, AnimationProperty property, MediaTime oldTime, MediaTime newTime, double? newValue = null)
    {
        LayerId = layerId;
        Property = property;
        OldTime = oldTime;
        NewTime = newTime;
        NewValue = newValue;
    }

    public Guid LayerId { get; }
    public AnimationProperty Property { get; }
    public MediaTime OldTime { get; }
    public MediaTime NewTime { get; }
    public double? NewValue { get; }

    /// <summary>选择接收方完成草稿验证后允许控件开始手势。</summary>
    public bool SelectionAccepted { get; set; }
}
