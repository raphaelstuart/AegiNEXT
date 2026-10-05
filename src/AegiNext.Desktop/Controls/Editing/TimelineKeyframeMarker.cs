using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineKeyframeMarker(TimelineKeyframeIdentity Identity, AnimationValue Value,
    TimelineComponentMask Components, Point Position, Rect Curve, double Minimum, double Maximum);
