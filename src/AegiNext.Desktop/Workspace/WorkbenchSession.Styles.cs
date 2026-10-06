using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal SubtitleFontSelectionService Fonts => applicationContext.Fonts;
    private readonly Rendering.LayerPlacementResolver layerPlacement = new();
    private Exception? placementDiagnostic;

    internal SubtitlePositionMeasurement MeasureStylePosition(SubtitleStylePreset preset)
    {
        var document = Editor.Snapshot;
        var text = SelectedCue?.Text ?? Localization.Get("Workbench.SubtitlePreviewText");
        return Rendering.SubtitleStylePositionMeasurer.Measure(preset, document.Width, document.Height, text, Fonts.Catalog);
    }
    internal void NotifyStyleLibraryChanged() => StyleLibraryChanged?.Invoke(this, EventArgs.Empty);
    internal Task ApplySelectedStyleAsync()
    {
        return ViewModel.Styles.SelectedPreset is { } selected
            ? styles.ApplyAsync(styleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id))
            : Task.CompletedTask;
    }
}
