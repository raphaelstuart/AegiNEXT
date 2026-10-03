using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed class StyleLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    public Task Completion { get; private set; } = Task.CompletedTask;
    private int queuedOperations;
    internal bool IsBusy => queuedOperations > 0;
    internal void Initialize() => Queue(async () =>
    {
        await session.StyleLibrary.LoadAsync();
        Refresh();
    });

    internal void Queue(Func<Task> action)
    {
        queuedOperations++;
        Completion = RunAsync(Completion, action);
        session.NotifyStyleLibraryChanged();
    }

    private async Task RunAsync(Task previous, Func<Task> action)
    {
        try
        {
            await previous;
            if (!session.IsClosing)
            {
                await session.RunCommandAsync(action);
            }
        }
        finally
        {
            queuedOperations--;
            session.NotifyStyleLibraryChanged();
        }
    }

    internal async Task UpsertAsync(SubtitleStylePreset preset)
    {
        await session.StyleLibrary.UpsertAsync(preset);
        Refresh(preset.Id);
    }

    internal async Task DeleteAsync(Guid id)
    {
        await session.StyleLibrary.RemoveAsync(id);
        Refresh();
    }

    internal void Refresh(Guid? selectedId = null)
    {
        if (session.IsClosing)
        {
            return;
        }

        var vm = session.ViewModel.Styles;
        var id = selectedId ?? vm.SelectedPreset?.Id;
        vm.Presets = session.StyleLibrary.Snapshot.Presets.Select(value => new StylePresetListItem(value.Id, value.Name)).ToArray();
        vm.SelectedPreset = vm.Presets.FirstOrDefault(value => value.Id == id) ?? vm.Presets.FirstOrDefault();
        vm.CanApplyPreset = session.HasSelectedCue && vm.Presets.Length > 0;
        session.NotifyStyleLibraryChanged();
    }

    internal async Task CaptureAsync()
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts() || session.SelectedCue is not { } cue)
        {
            return;
        }

        session.SetProjectBusy(true);
        try
        {
            var index = 1;
            var name = $"{WorkbenchText.Get("CapturedStyle")} {index}";
            while (session.StyleLibrary.Snapshot.Presets.Any(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{WorkbenchText.Get("CapturedStyle")} {++index}";
            }

            var preset = await SubtitleStylePresetService.CaptureAsync(name, cue.Style, session.Editor.Snapshot, session.ProjectDirectory);
            await UpsertAsync(preset);
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task ApplyAsync(SubtitleStylePreset preset)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts() || session.SelectedCue is not { } cue)
        {
            return;
        }

        session.SetProjectBusy(true);
        try
        {
            var prepared = await SubtitleStylePresetService.ApplyAsync(preset, session.Editor.Snapshot, session.ProjectDirectory, [cue.Id]);
            session.Editor.Apply("Apply subtitle style preset", _ => prepared);
            Refresh(preset.Id);
        }
        finally
        {
            session.SetProjectBusy(false);
        }

        if (session.Controller.Snapshot.State == AegiNext.Media.Playback.VideoPlaybackState.PAUSED)
        {
            await session.Controller.SeekAsync(session.Controller.Snapshot.Position);
        }
    }

    internal async Task ImportAsync()
    {
        var path = await dialogs.OpenFileAsync("ImportStyles", "StyleFiles", ["*.aegistyles"]);
        if (path is not null)
        {
            await session.StyleLibrary.ImportAsync(path);
            Refresh();
        }
    }

    internal async Task ExportAsync()
    {
        var path = await dialogs.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "styles.aegistyles");
        if (path is not null)
        {
            await session.StyleLibrary.ExportAsync(path);
        }
    }
}
