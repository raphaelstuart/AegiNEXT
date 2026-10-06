using System.Collections.Immutable;

namespace AegiNext.Rendering.Fonts;

internal sealed record OpenTypeNamedInstance(int Index, string Name, string? PostScriptName, ImmutableDictionary<uint, float> Coordinates);
