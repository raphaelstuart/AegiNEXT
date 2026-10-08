using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Presets;

internal sealed class SettingsStyleImportTask(DesktopApplicationContext owner, IReadOnlyList<string> paths) : AegiTask
{
    private readonly string[] inputs = paths.Select(Path.GetFullPath).ToArray();

    public override string Name => "Tasks.ImportStyles";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [owner.GetLibraryResource(PersonalLibraryKind.STYLE), .. inputs.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return owner.RunStyleOperationAsync(() => Task.Run(() => owner.StyleLibrary.ImportAsync(inputs,
            () => context.EnterCommit(), context.CancellationToken), context.CancellationToken));
    }
}
