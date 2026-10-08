using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Presets;

internal sealed class SettingsEffectExportTask(DesktopApplicationContext owner, ImmutableArray<EffectScriptPreset> selection,
    string destination, bool batch) : AegiTask
{
    private readonly IReadOnlyList<string> paths = batch
        ? PresetBatchExporter.GetEffectDestinations(selection, destination)
        : [Path.GetFullPath(destination)];

    public override string Name => "Tasks.ExportEffectScript";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. paths.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        var libraryResource = owner.GetCanonicalLibraryResource(PersonalLibraryKind.EFFECT);
        if (paths.Any(path => AegiTaskResource.StoragePath(path) == libraryResource))
        {
            throw new InvalidDataException("导出位置不能是正在使用的效果库文件。");
        }

        context.ReportProgress(new(Name));
        return Task.Run(() => batch
            ? PresetBatchExporter.ExportEffectsAsync(selection, destination, () => context.EnterCommit(), context.CancellationToken)
            : EffectScriptPresetStore.WriteScriptAsync(selection.Single().Source, destination,
                () => context.EnterCommit(), context.CancellationToken), context.CancellationToken);
    }
}
