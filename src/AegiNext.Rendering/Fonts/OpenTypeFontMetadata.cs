using System.Collections.Immutable;

namespace AegiNext.Rendering.Fonts;

internal sealed record OpenTypeFontMetadata
{
    public string? FamilyName { get; init; }
    public ImmutableArray<string> FamilyAliases { get; init; } = [];
    public string? SubfamilyName { get; init; }
    public string? PostScriptName { get; init; }
    public int? Weight { get; init; }
    public int? Width { get; init; }
    public bool? Italic { get; init; }
    public ImmutableArray<OpenTypeVariationAxis> Axes { get; init; } = [];
    public ImmutableArray<OpenTypeNamedInstance> NamedInstances { get; init; } = [];
}
