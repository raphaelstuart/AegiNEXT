using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ClipClipboardContent? timelineClipboard;

    internal bool CanCopyTimelineClips => !closing && !projectBusy && TimelineClipIds().Length > 0;
    internal bool CanPasteTimelineClips => !closing && !projectBusy && timelineClipboard is { } content &&
        content.SourceProjectId == editor.Snapshot.Id;

    internal ImmutableArray<Guid> TimelineClipIds()
    {
        var selected = ViewModel.Effects.SelectedIds.ToHashSet();
        if (SelectedLayerId is { } primary)
        {
            selected.Add(primary);
        }

        return [.. Flatten(editor.Snapshot.Layers).Where(layer => layer.Kind != LayerKind.GROUP && selected.Contains(layer.Id))
            .Select(layer => layer.Id)];
    }

    internal bool SelectTimelineLayers(TimelineSelectionEventArgs value)
    {
        if (updatingWorkbench || projectBusy || closing || !TryCommitDrafts())
        {
            RefreshDocument();
            return false;
        }

        SelectLayer(value.Id, value.SelectedIds.ToArray());
        return SelectedLayerId == value.Id && ViewModel.Effects.SelectedIds.ToHashSet().SetEquals(value.SelectedIds);
    }

    internal Task CommitTimelineClipsMoveAsync(TimelineClipsMoveEventArgs value) =>
        RunCommandAsync(() => EditAsync(() =>
        {
            editor.ShiftClips(value.LayerIds, value.Offset);
            SelectLayer(value.PrimaryId, value.LayerIds.ToArray());
        }));

    internal Task CopyTimelineClipsAsync(Guid primaryId, IReadOnlyCollection<Guid> layerIds, ProjectDocument? expected = null) =>
        RunCommandAsync(() =>
        {
            if (TimelineContextIsCurrent(expected))
            {
                return EditAsync(() => timelineClipboard = ProjectEditingOperations.CaptureClips(editor.Snapshot, layerIds, primaryId, CurrentTrackId));
            }
            return Task.CompletedTask;
        });

    internal Task PasteTimelineClipsAsync(MediaTime time, ProjectDocument? expected = null) =>
        PasteTimelineClipsAsync(time, null, false, expected);

    internal Task PasteTimelineClipsAtTargetAsync(TimelineClipContextEventArgs target, ProjectDocument? expected = null) =>
        PasteTimelineClipsAsync(target.Time, target.TrackId, true, expected);

    private Task PasteTimelineClipsAsync(MediaTime time, Guid? targetTrackId, bool requiresTargetTrack, ProjectDocument? expected) =>
        RunCommandAsync(() =>
        {
            if (!CanPasteTimelineClips || !TimelineContextIsCurrent(expected))
            {
                return Task.CompletedTask;
            }

            var content = timelineClipboard!;
            return EditAsync(() =>
            {
                try
                {
                    if (requiresTargetTrack && !content.Subtitles.IsEmpty && targetTrackId is null)
                    {
                        throw new InvalidOperationException(Localization.Get("Workbench.TimelinePasteFailed"));
                    }
                    var result = editor.PasteClips(content, time < MediaTime.Zero ? MediaTime.Zero : time, targetTrackId);
                    SelectLayer(result.PrimaryId, result.LayerIds.ToArray());
                }
                catch (Exception error) when (error is InvalidDataException or InvalidOperationException or KeyNotFoundException)
                {
                    throw new InvalidOperationException(Localization.Get("Workbench.TimelinePasteFailed"), error);
                }
            });
        });

    internal Task DeleteTimelineClipsAsync(IReadOnlyCollection<Guid> layerIds, ProjectDocument? expected = null) =>
        RunCommandAsync(() =>
        {
            if (!TimelineContextIsCurrent(expected))
            {
                return Task.CompletedTask;
            }

            return EditAsync(() =>
            {
                editor.RemoveClips(layerIds);
                RefreshDocument();
            });
        });

    internal Task CreateTimelineSubtitleAsync(Guid trackId, MediaTime time, ProjectDocument expected) =>
        RunCommandAsync(async () =>
        {
            if (!TimelineContextIsCurrent(expected) || projectBusy || !TryCommitDrafts())
            {
                return;
            }

            var start = time < MediaTime.Zero ? MediaTime.Zero : time;
            var cue = new SubtitleLine { TrackId = trackId, Start = start, End = start + new MediaTime(2) };
            var presetId = ViewModel.Styles.SelectedPreset?.Id;
            var snapshot = editor.Snapshot;
            SetProjectBusy(true);
            try
            {
                if (snapshot.Subtitles.Any(line => line.TrackId == trackId && start < line.End && line.Start < cue.End))
                {
                    throw new InvalidOperationException(Localization.Get("Workbench.TimelineClipCollision"));
                }
                await CreateSubtitleClipsAsync([cue], trackId, presetId);
            }
            finally
            {
                SetProjectBusy(false);
            }
            SelectCue(cue.Id);
        });

    internal void ClearTimelineClipboard() => timelineClipboard = null;

    private bool TimelineContextIsCurrent(ProjectDocument? expected) => !closing && !projectBusy &&
        (expected is null || ReferenceEquals(expected, editor.Snapshot));
}
