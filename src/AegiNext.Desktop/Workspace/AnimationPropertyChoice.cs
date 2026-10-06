using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record AnimationPropertyChoice(AnimationProperty Property, string Title)
{
    /// <inheritdoc />
    public override string ToString() => Title;
}
