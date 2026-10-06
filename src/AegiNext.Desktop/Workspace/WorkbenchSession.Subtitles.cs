using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Shortcuts.TimingEnterResult? pendingTimingEntry;
    private MediaTime? pendingTimingEnd;

    internal async Task AddCueAsync()
    {
        if (CurrentTrackId is not { } trackId)
        {
            return;
        }
        var start = ProjectPosition < MediaTime.Zero ? MediaTime.Zero : ProjectPosition;
        var presetId = ViewModel.Styles.SelectedPreset?.Id;
        var cue = new SubtitleLine { Start = start, End = start + new MediaTime(2), Text = string.Empty, TrackId = trackId };
        SetProjectBusy(true);
        try
        {
            await CreateSubtitleClipsAsync([cue], trackId, presetId);
        }
        finally
        {
            SetProjectBusy(false);
        }

        SelectCue(cue.Id);
    }

    internal async Task SetCueStartAsync()
    {
        if (CurrentTrackId is not { } trackId)
        {
            return;
        }
        var start = ProjectPosition < MediaTime.Zero ? MediaTime.Zero : ProjectPosition;
        var entered = timingSession.Enter(start);
        if (entered.IsRepeated)
        {
            return;
        }

        var presetId = ViewModel.Styles.SelectedPreset?.Id;
        pendingTimingEntry = entered;
        pendingTimingEnd = null;
        SetProjectBusy(true);
        try
        {
            await CreateSubtitleClipsAsync([
                new()
                {
                    Id = entered.CueId, TrackId = trackId, Start = entered.Start, End = entered.Start + new MediaTime(2),
                    Text = string.Empty
                }
            ], trackId, presetId);
        }
        catch
        {
            pendingTimingEnd = null;
            throw;
        }
        finally
        {
            pendingTimingEntry = null;
            SetProjectBusy(false);
        }

        SelectCue(entered.CueId);
        SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        timingSession = entered.Session;
        if (pendingTimingEnd is { } end)
        {
            pendingTimingEnd = null;
            SetCueEndAt(end);
        }

        ViewModel.RefreshCommands();
    }

    internal async Task CreateSubtitleClipsAsync(IEnumerable<SubtitleLine> lines, Guid trackId, Guid? fallbackPresetId)
    {
        var imported = lines.ToArray();
        if (imported.Length == 0)
        {
            return;
        }

        var prepared = await styles.PrepareCreationAsync(trackId, fallbackPresetId);
        editor.Apply("Create subtitle clips", _ =>
            ProjectEditingOperations.CreateSubtitleClips(prepared.Project, imported, trackId, prepared.Style));
    }

    internal void SetCueEnd()
    {
        SetCueEndAt(ProjectPosition);
    }

    private void SetCueEndAt(MediaTime end)
    {
        if (timingSession.Exit(end) is not { } exited)
        {
            return;
        }

        editor.SetSubtitleTiming(exited.CueId, exited.Start, exited.End, TimelineEditMode.CROP);
        timingSession = exited.Session;
        SelectCue(exited.CueId);
        ViewModel.RefreshCommands();
    }


    internal void SplitCue()
    {
        var cue = SelectedCue ?? throw new InvalidOperationException(Localization.Get("Workbench.NoSelection"));
        var boundaries = StringInfo.ParseCombiningCharacters(cue.Text).Append(cue.Text.Length).ToArray();
        if (!textCarets.TryGetValue(cue.Id, out var caret))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SplitCaret"));
        }

        var offset = boundaries.MinBy(value => Math.Abs(value - caret));


        editor.Apply("Split subtitle",
            document => ProjectEditingOperations.SplitSubtitle(document, cue.Id, ProjectPosition, offset));
    }

    internal void MergeCue()
    {
        var targets = MergeSubtitleTargets();
        if (targets.Length < 2)
        {
            return;
        }

        var prepared = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, targets);
        var merged = prepared.Subtitles.First(line => targets.Contains(line.Id));
        editor.Apply("Merge subtitles", _ => prepared);
        SelectCue(merged.Id);
    }

}
