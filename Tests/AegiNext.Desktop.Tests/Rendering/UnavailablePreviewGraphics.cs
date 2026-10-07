using Avalonia;
using Avalonia.OpenGL;

namespace AegiNext.Desktop.Tests.Rendering;

internal sealed class UnavailablePreviewGraphics : IOpenGlTextureSharingRenderInterfaceContextFeature
{
    internal int Attempts { get; private set; }
    public bool CanCreateSharedContext => true;

    public IGlContext? CreateSharedContext(IEnumerable<GlVersion>? preferredVersions = null)
    {
        Attempts++;
        throw new NotSupportedException("The test graphics device is unavailable.");
    }

    public ICompositionImportableOpenGlSharedTexture CreateSharedTextureForComposition(IGlContext context, PixelSize size) =>
        throw new NotSupportedException();
}
