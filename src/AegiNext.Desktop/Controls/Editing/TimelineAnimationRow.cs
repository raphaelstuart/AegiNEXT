using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineAnimationRow(AnimationTrackTarget Target, double Top, double Height)
{
    internal AnimationProperty Property => Target.Property;
}
