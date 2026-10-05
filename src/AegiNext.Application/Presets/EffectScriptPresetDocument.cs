using System.Collections.Immutable;

namespace AegiNext.Application.Presets;

/// <summary>独立于工程文件的版本化个人特效脚本库。</summary>
public sealed record EffectScriptPresetDocument
{
    public int Version { get; init; } = 1;
    public ImmutableArray<EffectScriptPreset> Presets { get; init; } = [];
}
