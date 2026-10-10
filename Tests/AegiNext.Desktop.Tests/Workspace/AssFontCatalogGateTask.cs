using AegiNext.Application.Tasks;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class AssFontCatalogGateTask : AegiTask<SystemFontCatalog>
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string Name => "ASS font catalog test gate";
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Named("system-font-catalog")];

    protected override async Task<SystemFontCatalog> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        Entered.TrySetResult();
        await Released.Task.WaitAsync(context.CancellationToken);
        return SystemFontCatalog.Empty;
    }
}
