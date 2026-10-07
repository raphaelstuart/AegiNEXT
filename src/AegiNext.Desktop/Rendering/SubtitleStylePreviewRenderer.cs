using AegiNext.Core.Timing;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal static class SubtitleStylePreviewRenderer
{
    private const int OUTPUT_WIDTH = 640;
    private const int OUTPUT_HEIGHT = 360;
    private const byte BACKGROUND_COMPONENT = 64;
    private static readonly byte[] background = CreateBackground();

    internal static SdrVideoFrame Render(SubtitleStylePreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scene = SubtitleStylePreviewScene.Create(request.Preset, request.CanvasWidth, request.CanvasHeight, request.Text);
        using var renderer = new ProjectSceneRenderer(scene.Assets, request.FontCatalog);
        var pixels = renderer.ComposePreview(scene.Document, MediaTime.Zero, background,
            OUTPUT_WIDTH, OUTPUT_HEIGHT, OUTPUT_WIDTH * 4, OUTPUT_WIDTH, OUTPUT_HEIGHT, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(OUTPUT_WIDTH, OUTPUT_HEIGHT, pixels);
    }

    private static byte[] CreateBackground()
    {
        var pixels = new byte[OUTPUT_WIDTH * OUTPUT_HEIGHT * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = pixels[index + 1] = pixels[index + 2] = BACKGROUND_COMPONENT;
            pixels[index + 3] = byte.MaxValue;
        }
        return pixels;
    }
}
