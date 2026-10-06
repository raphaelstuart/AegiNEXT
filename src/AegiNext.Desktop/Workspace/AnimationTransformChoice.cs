namespace AegiNext.Desktop.Workspace;

internal sealed record AnimationTransformChoice(Guid Id, string Title)
{
    public override string ToString() => Title;
}
