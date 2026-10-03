namespace AegiNext.Desktop.Layouts;

internal sealed record LayoutNodeSnapshot
{
    public string Kind { get; init; } = "tabs";
    public string? PanelId { get; init; }
    public string Orientation { get; init; } = "horizontal";
    public double Proportion { get; init; } = 1;
    public string? ActivePanelId { get; init; }
    public IReadOnlyList<LayoutNodeSnapshot> Children { get; init; } = [];
}
