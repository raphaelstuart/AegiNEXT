using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private readonly Rendering.LayerPlacementResolver layerPlacement = new();
    private Exception? placementDiagnostic;

    internal SubtitlePositionMeasurement MeasureStylePosition(SubtitleStylePreset preset)
    {
        var document = Editor.Snapshot;
        var text = SelectedCue?.Text ?? WorkbenchText.Get("SubtitlePreviewText");
        return Rendering.SubtitleStylePositionMeasurer.Measure(preset, document.Width, document.Height, text);
    }
    internal void NotifyStyleLibraryChanged() => StyleLibraryChanged?.Invoke(this, EventArgs.Empty);
    internal Task ApplySelectedStyleAsync()
    {
        return ViewModel.Styles.SelectedPreset is { } selected
            ? styles.ApplyAsync(styleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id))
            : Task.CompletedTask;
    }
}
