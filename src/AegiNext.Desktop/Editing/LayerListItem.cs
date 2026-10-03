namespace AegiNext.Desktop.Editing;

internal sealed record LayerListItem(Guid Id, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
