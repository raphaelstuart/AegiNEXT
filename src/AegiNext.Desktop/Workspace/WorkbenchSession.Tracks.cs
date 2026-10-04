using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid currentTrackId = SubtitleTrack.DEFAULT_TRACK_ID;

    internal Guid CurrentTrackId => editor.Snapshot.SubtitleTracks.Any(track => track.Id == currentTrackId)
        ? currentTrackId : editor.Snapshot.SubtitleTracks[0].Id;

    internal void SelectTrack(Guid trackId)
    {
        if (updatingWorkbench || projectBusy || CurrentTrackId == trackId)
        {
            return;
        }
        if (!editor.Snapshot.SubtitleTracks.Any(track => track.Id == trackId))
        {
            throw new KeyNotFoundException("字幕轨道不存在。");
        }
        if (!TryCommitDrafts())
        {
            RefreshSubtitleTracks();
            return;
        }

        ViewModel.CancelGestures();
        currentTrackId = trackId;
        SelectedCueId = editor.Snapshot.Subtitles.Where(line => line.TrackId == trackId).OrderBy(line => line.Start).FirstOrDefault()?.Id;
        SelectedLayerId = SelectedCueId is { } cueId
            ? Flatten(editor.Snapshot.Layers).Single(layer => layer.SubtitleId == cueId).Id : null;
        SelectedKeyTime = null;
        timingSession = timingSession.Reset();
        RefreshDocument();
    }

    internal void RefreshSubtitleTracks()
    {
        currentTrackId = CurrentTrackId;
        ViewModel.Subtitles.UpdateTracks(editor.Snapshot.SubtitleTracks, currentTrackId);
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
        var prefix = WorkbenchText.Get("SubtitleTrack");
        var names = editor.Snapshot.SubtitleTracks.Select(track => track.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (names.Contains($"{prefix} {number}"))
        {
            number++;
        }

        var id = editor.AddSubtitleTrack($"{prefix} {number}");
        SelectTrack(id);
    }

    internal void RenameSubtitleTrack()
    {
        editor.RenameSubtitleTrack(CurrentTrackId, ViewModel.Subtitles.TrackName);
        ViewModel.Subtitles.AcceptTrackName();
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
            SelectedCueId = subtitleId;
            SelectedLayerId = Flatten(editor.Snapshot.Layers).Single(layer => layer.SubtitleId == subtitleId).Id;
            SelectedKeyTime = null;
            RefreshDocument();
        }));
    }
}
