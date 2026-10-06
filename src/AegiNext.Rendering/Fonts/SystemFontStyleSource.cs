namespace AegiNext.Rendering.Fonts;

internal sealed record SystemFontStyleSource
{
    public string FamilyName { get; init; } = string.Empty;
    public string SourceFamilyName { get; init; } = string.Empty;
    public int StyleIndex { get; init; }
    public string? StyleName { get; init; }
    public string? PostScriptName { get; init; }
    public int Weight { get; init; } = 400;
    public int Width { get; init; } = 5;
    public bool Italic { get; init; }
    public int FontIndex { get; init; }
}
