namespace AegiNext.Desktop.Layouts;

internal sealed record LayoutPresetRow(string Id, string DisplayName, bool IsReadOnly)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return DisplayName;
    }
}
