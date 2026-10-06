namespace AegiNext.Desktop.Workspace;

internal sealed record MaskSelectionChoice(Guid Id, string Title)
{
    public override string ToString() => Title;
}
