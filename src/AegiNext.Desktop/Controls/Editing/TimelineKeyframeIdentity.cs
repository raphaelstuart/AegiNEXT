using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal readonly record struct TimelineKeyframeIdentity(Guid LayerId, AnimationTrackTarget Target, MediaTime Time, Guid? OperationId = null, bool IsOperationStart = false)
{
    internal TimelineKeyframeIdentity(Guid layerId, AnimationProperty property, MediaTime time) : this(layerId, new AnimationTrackTarget(property), time)
    {
    }
    internal AnimationProperty Property => Target.Property;
}
