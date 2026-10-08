using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Presets;

internal sealed class SettingsEffectImportTask(DesktopApplicationContext owner, IReadOnlyList<string> paths) : AegiTask
{
    private readonly string[] inputs = paths.Select(Path.GetFullPath).ToArray();

    public override string Name => "Tasks.ImportEffectScripts";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [owner.GetLibraryResource(PersonalLibraryKind.EFFECT), .. inputs.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return owner.RunEffectOperationAsync(() => Task.Run(() => owner.EffectScriptLibrary.ImportAsync(inputs,
            () => context.EnterCommit(), context.CancellationToken), context.CancellationToken));
    }
}
