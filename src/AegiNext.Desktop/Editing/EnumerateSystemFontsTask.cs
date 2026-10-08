using AegiNext.Application.Tasks;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Desktop.Editing;

internal sealed class EnumerateSystemFontsTask : AegiTask<SystemFontCatalog>
{
    public override string Name => "Tasks.SystemFonts";
    public override bool CanCancel => true;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Named("system-font-catalog")];

    protected override Task<SystemFontCatalog> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return Task.Run(() => new SystemFontCatalog(SKFontManager.Default, cancellationToken: context.CancellationToken),
            context.CancellationToken);
    }
}
