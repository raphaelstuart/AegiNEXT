using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record AnimationPropertyChoice(AnimationTrackTarget Target, string Title)
{
    internal AnimationPropertyChoice(AnimationProperty property, string title) : this(new AnimationTrackTarget(property), title)
    {
    }
    public AnimationProperty Property => Target.Property;
    /// <inheritdoc />
    public override string ToString() => Title;
}
