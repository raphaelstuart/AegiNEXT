using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed record SubtitleStylePreviewScene(ProjectDocument Document, IProjectAssetResolver Assets)
{
    internal static SubtitleStylePreviewScene Create(SubtitleStylePreset preset, int width, int height, string text)
    {
        SubtitleStylePresetValidator.Validate(preset with { Name = "Preview", TimingPostProcessor = null });
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
        return new(document, resolver);
    }
}
