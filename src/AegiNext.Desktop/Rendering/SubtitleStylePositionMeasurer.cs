using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal static class SubtitleStylePositionMeasurer
{
    internal static SubtitlePositionMeasurement Measure(SubtitleStylePreset preset, int width, int height, string text,
        SystemFontCatalog? fontCatalog = null)
    {
        try
        {
            var scene = SubtitleStylePreviewScene.Create(preset, width, height, text);
            using var renderer = new ProjectSceneRenderer(scene.Assets, fontCatalog);
            var measurement = renderer.MeasureSubtitlePlacement(scene.Document, scene.Document.Subtitles[0]);
            return new(measurement.Position, new(new(width, height),
                new(measurement.Bounds.Left, measurement.Bounds.Top),
                new(measurement.Bounds.Width, measurement.Bounds.Height), new(), measurement.HasInk));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new(null, null, error.Message);
        }
    }
}
