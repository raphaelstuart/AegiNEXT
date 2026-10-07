using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests.Rendering;

public sealed class ProjectPreviewGpuFallbackTests
{
    [PreviewQualityNativeFact]
    public async Task LateUnavailableGraphicsRetriesOnlyOnceAndKeepsRenderingAndIdentityOnCpu()
    {
        using var fixture = await PreviewQualityFixture.CreateAsync(320, 180);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var document = new ProjectDocument { Width = 320, Height = 180, Layers = [new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 320, 180), Fill = new(0.25, 0.5, 0.75)
        }] };
        var state = new ProjectPreviewState(document, fixture.DirectoryPath);
        var catalog = new PreviewFrameCatalog();
        var unavailable = new UnavailablePreviewGraphics();
        var graphicsReady = false;
        Exception? renderError = null;
        using var converter = new ProjectPreviewConverter(() => state, error => renderError = error,
            previewFrames: catalog, getGraphics: () => graphicsReady ? unavailable : null);
        var first = converter.Convert(frame);
        Assert.False(converter.UsesGpu);
        Assert.Equal(0, unavailable.Attempts);
        graphicsReady = true;
        var fallback = converter.Convert(frame);
        Assert.Equal(first.Pixels.ToArray(), fallback.Pixels.ToArray());
        Assert.Equal(1, unavailable.Attempts);
        Assert.Null(renderError);
        Assert.Same(document, catalog.FindIdentity(fallback)!.Document);
        state = state with { Document = document with { Layers = [document.Layers[0] with { Fill = new(1, 0, 0) }] }, QualityRevision = 1 };
        var changed = converter.Convert(frame);
        Assert.Equal(1, unavailable.Attempts);
        Assert.False(converter.UsesGpu);
        Assert.Null(renderError);
        Assert.Same(state.Document, catalog.FindIdentity(changed)!.Document);
        Assert.Equal(1, catalog.FindIdentity(changed)!.QualityRevision);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, changed.Pixels.Span[..4].ToArray());
    }
}
