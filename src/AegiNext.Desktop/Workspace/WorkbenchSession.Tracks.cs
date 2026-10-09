using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid? currentTrackId = ProjectTrack.DEFAULT_TRACK_ID;

    internal Guid? CurrentTrackId => editor.Snapshot.Tracks.Any(track => track.Id == currentTrackId)
        ? currentTrackId : editor.Snapshot.Tracks.FirstOrDefault()?.Id;

    internal Task ApplySubtitleTrackStyleAsync(Guid trackId, Guid presetId)
    {
        return RunCommandAsync(() => styles.ApplyTrackAsync(trackId, presetId));
    }

    internal Task ToggleTrackAutoApplyStyleAsync(Guid trackId)
    {
        return RunCommandAsync(() => EditAsync(() =>
        {
            var track = editor.Snapshot.Tracks.Single(value => value.Id == trackId);
            editor.SetSubtitleTrackAutoApplyStyle(trackId, !track.AutoApplyStyle);
        }));
    }

    internal bool SelectTrack(Guid trackId)
    {
        if (IsUpdating || IsProjectBusy)
        {
            return false;
        }
        if (!editor.Snapshot.Tracks.Any(track => track.Id == trackId))
        {
            throw new KeyNotFoundException("轨道不存在。");
        }
        if (!TryCommitDrafts())
        {
            RefreshSubtitleTracks();
            return false;
        }

        ViewModel.Timeline.ClearTrackSoloForTrackSelection(trackId);
        ViewModel.CancelGestures();
        InvalidateTimingSession();
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
        ViewModel.Subtitles.UpdateTracks(editor.Snapshot.Tracks, currentTrackId);
        ViewModel.Timeline.SelectedTrackId = currentTrackId;
    }

    internal void SyncCurrentTrackForSelection()
    {
        ViewModel.Timeline.ValidateTrackSoloSelection();
        if (SelectedLayer is { } clip)
        {
            currentTrackId = clip.TrackId;
        }

        RefreshSubtitleTracks();
    }

    internal void AddSubtitleTrack()
    {
        var prefix = Localization.Get("Workbench.SubtitleTrack");
        var names = editor.Snapshot.Tracks.Select(track => track.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (names.Contains($"{prefix} {number}"))
        {
            number++;
        }

        var id = editor.AddTrack($"{prefix} {number}");
        SelectTrack(id);
    }

    internal void RenameSubtitleTrack(Guid trackId, string name)
    {
        editor.RenameTrack(trackId, name);
        RefreshSubtitleTracks();
    }

    internal Task RemoveSubtitleTrackAsync()
    {
        return RunCommandAsync(async () =>
        {
            if (IsUpdating || IsProjectBusy || CurrentTrackId is not { } trackId)
            {
                return;
            }

            var source = editor.Snapshot;
            var generation = projectGeneration;
            var track = source.Tracks.Single(value => value.Id == trackId);
            var count = ClipIndex.GetTrackClips(trackId).Length;
            if (count > 0)
            {
                var accepted = false;
                {
                    using var editingLease = AcquireEditingLease();
                    accepted = await dialogs.ConfirmTrackDeletionAsync(track.Name, count, ProjectOperationsToken);
                }
                if (!accepted)
                {
                    return;
                }
            }

            if (generation != projectGeneration || !ReferenceEquals(source, editor.Snapshot))
            {
                return;
            }
            await EditAsync(() => editor.RemoveTrack(trackId));
        });
    }

    internal Task MoveCurrentSubtitleTrackAsync(int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return RunCommandAsync(() => EditAsync(() =>
        {
            if (CurrentTrackId is not { } trackId)
            {
                return;
            }
            var tracks = editor.Snapshot.Tracks;
            var index = tracks.IndexOf(tracks.Single(track => track.Id == trackId));
            var target = index + direction;
            if (target >= 0 && target < tracks.Length)
            {
                editor.MoveTrack(trackId, target);
            }
        }));
    }

    internal void MoveSubtitleToTrack(Guid subtitleId, Guid trackId)
    {
        editor.MoveSubtitleToTrack(subtitleId, trackId);
        currentTrackId = trackId;
        ResetSubtitleSelection(subtitleId);
        SelectedCueId = subtitleId;
        SelectedLayerId = ClipIndex.GetSubtitleClip(subtitleId).Id;
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
            SelectedLayerId = ClipIndex.GetSubtitleClip(subtitleId).Id;
            SelectedKeyTime = null;
            RefreshDocument();
        }));
    }

    internal Task MoveTrackAsync(Guid trackId, int newIndex, ProjectDocument expectedDocument)
    {
        return RunCommandAsync(() =>
        {
            if (!ReferenceEquals(expectedDocument, editor.Snapshot))
            {
                return Task.CompletedTask;
            }

            return EditAsync(() =>
            {
                if (ReferenceEquals(expectedDocument, editor.Snapshot))
                {
                    editor.MoveTrack(trackId, newIndex);
                }
            });
        });
    }

    internal Task CommitClipMoveAsync(Guid clipId, Guid trackId, MediaTime start, MediaTime end,
        TimelineEditMode mode, bool move, ProjectDocument? expectedDocument = null)
    {
        return RunCommandAsync(() =>
        {
            if (expectedDocument is not null && !ReferenceEquals(expectedDocument, editor.Snapshot))
            {
                return Task.CompletedTask;
            }

            return EditAsync(() =>
            {
                if (expectedDocument is not null && !ReferenceEquals(expectedDocument, editor.Snapshot))
                {
                    return;
                }

                editor.MoveClip(clipId, trackId, start, end, mode, move);
                currentTrackId = trackId;
                SelectLayer(clipId, [clipId]);
            });
        });
    }
}
