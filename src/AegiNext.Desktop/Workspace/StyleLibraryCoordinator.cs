using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class StyleLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    internal event EventHandler? BusyChanged;
    public Task Completion { get; private set; } = Task.CompletedTask;
    private int queuedOperations;
    internal bool IsBusy => queuedOperations > 0 || session.ApplicationContext.StylesBusy;
    internal void Initialize() => Queue(async () =>
    {
        await session.ApplicationContext.Initialization;
        Refresh();
        session.LogInfo("Styles", $"{Localization.Get("WorkflowLog.StyleLibraryLoaded")} ({session.StyleLibrary.Snapshot.Presets.Length})");
    });

    internal void Queue(Func<Task> action)
    {
        queuedOperations++;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        Completion = RunAsync(Completion, action);
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
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal async Task UpsertAsync(SubtitleStylePreset preset)
    {
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(preset));
        Refresh(preset.Id);
        session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleSaved"), preset.Name);
    }

    internal async Task DeleteAsync(Guid id)
    {
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == id);
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.RemoveAsync(id));
        Refresh();
        if (preset is not null)
        {
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleDeleted"), preset.Name);
        }
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
            var name = $"{Localization.Get("Workbench.CapturedStyle")} {index}";
            while (session.StyleLibrary.Snapshot.Presets.Any(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{Localization.Get("Workbench.CapturedStyle")} {++index}";
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
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleApplied"), preset.Name);
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

    internal async Task ApplyTrackAsync(Guid trackId, Guid presetId)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == presetId) ??
            throw new KeyNotFoundException("字幕样式预设不存在。");
        session.SetProjectBusy(true);
        try
        {
            var track = session.Editor.Snapshot.SubtitleTracks.FirstOrDefault(value => value.Id == trackId) ??
                throw new KeyNotFoundException("字幕轨道不存在。");
            var count = session.Editor.Snapshot.Subtitles.Count(line => line.TrackId == trackId);
            var decision = count == 0 ? TrackStyleUpdateDecision.DEFAULT_ONLY :
                await dialogs.ConfirmTrackStyleChangeAsync(track.Name, preset.Name, count);
            if (decision == TrackStyleUpdateDecision.CANCEL)
            {
                return;
            }

            var prepared = await SubtitleStylePresetService.PrepareAsync(preset, session.Editor.Snapshot, session.ProjectDirectory);
            session.Editor.Apply("Apply subtitle track style", _ =>
                AegiNext.Application.ProjectEditingOperations.SetSubtitleTrackStyle(prepared.Project, trackId, preset.Id,
                    preset.Name, prepared.Style, decision == TrackStyleUpdateDecision.UPDATE_EXISTING));
            Refresh();
            session.LogInfo("Styles", Localization.Get("Workbench.TrackStyleApplied"), preset.Name);
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

    internal Task<PreparedSubtitleStyle> PrepareCreationAsync(Guid? trackId, Guid? fallbackPresetId)
    {
        var project = session.Editor.Snapshot;
        var track = trackId is { } id ? project.SubtitleTracks.FirstOrDefault(value => value.Id == id) ??
            throw new KeyNotFoundException("字幕轨道不存在。") : null;
        if (track is { AutoApplyStyle: true, DefaultStyle: { } defaultStyle })
        {
            return Task.FromResult(new PreparedSubtitleStyle(project, defaultStyle, track.StylePresetName!));
        }

        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == fallbackPresetId);
        return preset is null ? Task.FromResult(new PreparedSubtitleStyle(project, new())) :
            SubtitleStylePresetService.PrepareAsync(preset, project, session.ProjectDirectory);
    }

    internal async Task ImportAsync()
    {
        var path = await dialogs.OpenFileAsync("ImportStyles", "StyleFiles", ["*.aegistyles"]);
        if (path is not null)
        {
            await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.ImportAsync(path));
            Refresh();
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StylesImported"), path);
        }
    }

    internal async Task ExportAsync()
    {
        var path = await dialogs.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "styles.aegistyles");
        if (path is not null)
        {
            await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.ExportAsync(path));
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StylesExported"), path);
        }
    }
}
