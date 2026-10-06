using System.Collections.Immutable;
using AegiNext.Application.Presets;

namespace AegiNext.Desktop.Settings;

/// <summary>左侧所选脚本的已保存快照，包含内置模板。</summary>
public sealed class SettingsEffectsExportEventArgs(ImmutableArray<EffectScriptPreset> presets) : EventArgs
{
    public ImmutableArray<EffectScriptPreset> Presets { get; } = presets;
}
