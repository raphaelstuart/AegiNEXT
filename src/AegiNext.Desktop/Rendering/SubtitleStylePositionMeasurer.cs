using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal static class SubtitleStylePositionMeasurer
{
    internal static SubtitlePositionMeasurement Measure(SubtitleStylePreset preset, int width, int height, string text)
    {
        try
        {
            SubtitleStylePresetValidator.Validate(preset with { Name = "Preview" });
            var fontId = Guid.NewGuid();
            var subtitle = new SubtitleLine
            {
                Text = text, Style = preset.Style with { FontAssetId = preset.Font is null ? null : fontId }
            };
            var document = new ProjectDocument
            {
                Width = width, Height = height, Subtitles = [subtitle],
                Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End }],
                Assets = preset.Font is { } font
                    ? [new(fontId, ProjectAssetKind.FONT, $"Fonts/{font.FileName}", font.Sha256)]
                    : []
            };
            IProjectAssetResolver resolver = preset.Font is { } embedded
                ? new EmbeddedPresetFontResolver(fontId, embedded)
                : new DirectoryProjectAssetResolver(Path.GetTempPath());
            using var renderer = new ProjectSceneRenderer(resolver);
            var measurement = renderer.MeasureSubtitlePlacement(document, subtitle);
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
