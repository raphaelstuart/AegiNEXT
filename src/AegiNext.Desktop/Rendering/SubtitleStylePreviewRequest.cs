using AegiNext.Core.Presets;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed record SubtitleStylePreviewRequest(long Revision, SubtitleStylePreset Preset, string Text,
    int CanvasWidth, int CanvasHeight, SystemFontCatalog? FontCatalog);
