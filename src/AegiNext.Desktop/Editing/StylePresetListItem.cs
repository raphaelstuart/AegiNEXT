namespace AegiNext.Desktop.Editing;

internal sealed record StylePresetListItem(Guid Id, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
