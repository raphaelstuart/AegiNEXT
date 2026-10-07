using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Layouts;
using AegiNext.Media.Encoding.Presets;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsBundle
{
    public WorkbenchPreferences Preferences { get; init; } = new();
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public EffectScriptPresetDocument Effects { get; init; } = new();
    public VideoExportPresetCollection ExportPresets { get; init; } = new();
    public WorkspaceLayoutFile Layouts { get; init; } = new();
}
