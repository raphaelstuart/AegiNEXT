using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

internal readonly record struct TimelineKeyframeIdentity(Guid LayerId, AnimationProperty Property, MediaTime Time);
