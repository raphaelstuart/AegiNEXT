using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectTrackDrawingOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackReorderUpdatesPreviewCachedFrameAndExportPixelsRegardlessOfClipStorageOrder(bool reverseClips)
    {
        var front = new ProjectTrack();
        var back = new ProjectTrack();
        var red = new ProjectLayer
        {
            TrackId = front.Id,
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 2, 2),
            Fill = new(1, 0, 0)
        };
        var blue = red with { Id = Guid.NewGuid(), TrackId = back.Id, Fill = new(0, 0, 1) };
        var document = new ProjectDocument
        {
            Width = 2,
            Height = 2,
            Tracks = [front, back],
            Layers = reverseClips ? [blue, red] : [red, blue]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var pixels = new float[16];
        var revision = renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, 0);
        Assert.Equal(new float[] { 1, 0, 0, 1 }, pixels[..4]);
        AssertPixels(renderer, document, [(Half)1, (Half)0, (Half)0, (Half)1], [0, 0, 255, 255]);

        var reordered = document with { Tracks = [back, front] };
        var changed = renderer.UpdateCachedFramePixels(reordered, MediaTime.Zero, pixels, revision.Revision);
        Assert.True(changed.Updated);
        Assert.True(changed.Revision > revision.Revision);
        Assert.Equal(new float[] { 0, 0, 1, 1 }, pixels[..4]);
        AssertPixels(renderer, reordered, [(Half)0, (Half)0, (Half)1, (Half)1], [255, 0, 0, 255]);

        renderer.UpdateCachedFramePixels(document, MediaTime.Zero, pixels, changed.Revision);
        Assert.Equal(new float[] { 1, 0, 0, 1 }, pixels[..4]);
    }

    private static void AssertPixels(ProjectSceneRenderer renderer, ProjectDocument document, Half[] exportPixel, byte[] previewPixel)
    {
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.Equal(exportPixel, pixels[..4]);
        Assert.Equal(previewPixel, renderer.ComposePreview(document, MediaTime.Zero, new byte[16], 2, 2, 8)[..4]);
    }
}
