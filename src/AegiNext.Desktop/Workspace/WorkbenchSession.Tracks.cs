using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid currentTrackId = SubtitleTrack.DEFAULT_TRACK_ID;

    internal Guid CurrentTrackId => editor.Snapshot.SubtitleTracks.Any(track => track.Id == currentTrackId)
        ? currentTrackId : editor.Snapshot.SubtitleTracks[0].Id;

    internal Task ApplySubtitleTrackStyleAsync(Guid trackId, Guid presetId)
    {
        return RunCommandAsync(() => styles.ApplyTrackAsync(trackId, presetId));
    }

    internal Task ToggleTrackAutoApplyStyleAsync(Guid trackId)
    {
        return RunCommandAsync(() => EditAsync(() =>
        {
            var track = editor.Snapshot.SubtitleTracks.Single(value => value.Id == trackId);
            editor.SetSubtitleTrackAutoApplyStyle(trackId, !track.AutoApplyStyle);
        }));
    }

    internal bool SelectTrack(Guid trackId)
    {
        if (updatingWorkbench || projectBusy)
        {
            return false;
        }
        if (!editor.Snapshot.SubtitleTracks.Any(track => track.Id == trackId))
        {
            throw new KeyNotFoundException("字幕轨道不存在。");
        }
        if (!TryCommitDrafts())
        {
            RefreshSubtitleTracks();
            return false;
        }

        ViewModel.CancelGestures();
        currentTrackId = trackId;
        ResetSubtitleSelection();
        SelectedCueId = null;
        SelectedLayerId = null;
        ViewModel.Effects.SelectedIds = [];
        SelectedKeyTime = null;
        timingSession = timingSession.Reset();
        RefreshDocument();
        return true;
    }

    internal void RefreshSubtitleTracks()
    {
        currentTrackId = CurrentTrackId;
        ViewModel.Subtitles.UpdateTracks(editor.Snapshot.SubtitleTracks, currentTrackId);
        ViewModel.Timeline.SelectedTrackId = currentTrackId;
    }

    internal void SyncCurrentTrackForSelection()
    {
        if (SelectedCue is { } cue)
        {
            currentTrackId = cue.TrackId;
        }

        RefreshSubtitleTracks();
    }

    internal void AddSubtitleTrack()
    {
        var prefix = Localization.Get("Workbench.SubtitleTrack");
        var names = editor.Snapshot.SubtitleTracks.Select(track => track.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (names.Contains($"{prefix} {number}"))
        {
            number++;
        }

        var id = editor.AddSubtitleTrack($"{prefix} {number}");
        SelectTrack(id);
    }

    internal void RenameSubtitleTrack(Guid trackId, string name)
    {
        editor.RenameSubtitleTrack(trackId, name);
        RefreshSubtitleTracks();
    }

    internal void RemoveSubtitleTrack()
    {
        editor.RemoveSubtitleTrack(CurrentTrackId);
        RefreshSubtitleTracks();
    }

    internal Task MoveCurrentSubtitleTrackAsync(int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return RunCommandAsync(() => EditAsync(() =>
        {
            var tracks = editor.Snapshot.SubtitleTracks;
            var index = tracks.IndexOf(tracks.Single(track => track.Id == CurrentTrackId));
            var target = index + direction;
            if (target >= 0 && target < tracks.Length)
            {
                editor.MoveSubtitleTrack(CurrentTrackId, target);
            }
        }));
    }

    internal void MoveSubtitleToTrack(Guid subtitleId, Guid trackId)
    {
        editor.MoveSubtitleToTrack(subtitleId, trackId);
        currentTrackId = trackId;
        ResetSubtitleSelection(subtitleId);
        SelectedCueId = subtitleId;
        SelectedLayerId = Flatten(editor.Snapshot.Layers).Single(layer => layer.SubtitleId == subtitleId).Id;
        SelectedKeyTime = null;
        RefreshDocument();
    }

    internal Task CommitSubtitleClipMoveAsync(Guid subtitleId, Guid trackId, MediaTime start, MediaTime end,
        TimelineEditMode mode, bool move)
    {
        return RunCommandAsync(() => EditAsync(() =>
        {
            editor.MoveSubtitleClip(subtitleId, trackId, start, end, mode, move);
            currentTrackId = trackId;
            ResetSubtitleSelection(subtitleId);
            SelectedCueId = subtitleId;
            SelectedLayerId = Flatten(editor.Snapshot.Layers).Single(layer => layer.SubtitleId == subtitleId).Id;
            SelectedKeyTime = null;
            RefreshDocument();
        }));
    }
}
