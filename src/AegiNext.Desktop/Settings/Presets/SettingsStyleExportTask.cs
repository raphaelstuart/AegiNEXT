using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Presets;

internal sealed class SettingsStyleExportTask(DesktopApplicationContext owner, ImmutableArray<SubtitleStylePreset> selection,
    string destination, bool batch) : AegiTask
{
    private readonly IReadOnlyList<string> paths = batch
        ? PresetBatchExporter.GetStyleDestinations(selection, destination)
        : [Path.GetFullPath(destination)];

    public override string Name => "Tasks.ExportStyles";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. paths.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        var libraryResource = owner.GetCanonicalLibraryResource(PersonalLibraryKind.STYLE);
        if (paths.Any(path => AegiTaskResource.StoragePath(path) == libraryResource))
        {
            throw new InvalidDataException("导出位置不能是正在使用的样式库文件。");
        }

        context.ReportProgress(new(Name));
        return Task.Run(() => batch
            ? PresetBatchExporter.ExportStylesAsync(selection, destination, () => context.EnterCommit(), context.CancellationToken)
            : SubtitleStylePresetStore.SaveAsync(new() { Presets = selection }, destination,
                () => context.EnterCommit(), context.CancellationToken), context.CancellationToken);
    }
}
