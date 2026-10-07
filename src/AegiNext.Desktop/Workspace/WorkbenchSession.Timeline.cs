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
        content.SourceProjectId == editor.Snapshot.Id && (content.Subtitles.IsEmpty || CurrentTrackId.HasValue);

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

    internal Task ClearTimelineClipAnimationTracksAsync(IReadOnlyCollection<Guid> layerIds,
        ProjectDocument? expected = null)
    {
        var selection = layerIds.ToArray();
        return RunCommandAsync(() => TimelineContextIsCurrent(expected)
            ? EditAsync(() => ClearTimelineAnimationTracks(selection, null)) : Task.CompletedTask);
    }

    internal Task ClearTimelineAnimationRowAsync(TimelineAnimationRowId row, ProjectDocument expected,
        IReadOnlyCollection<Guid>? layerIds = null)
    {
        var selection = layerIds?.ToArray();
        return RunCommandAsync(() =>
        {
            if (!TimelineContextIsCurrent(expected))
            {
                return Task.CompletedTask;
            }
            var targets = selection ?? TimelineAnimationRowLayerIds(expected, row).ToArray();
            return EditAsync(() => ClearTimelineAnimationTracks(targets, row.Property));
        });
    }

    internal Task ClearTimelineClipAnimationPropertyTracksAsync(Guid layerId, AnimationProperty property,
        ProjectDocument expected) => RunCommandAsync(() => TimelineContextIsCurrent(expected)
        ? EditAsync(() => ClearTimelineAnimationTracks([layerId], property)) : Task.CompletedTask);

    internal static ImmutableArray<Guid> TimelineAnimationRowLayerIds(ProjectDocument source, TimelineAnimationRowId row)
    {
        if (row.Scope == TimelineRowScope.SUBTITLE_TRACK)
        {
            if (!source.SubtitleTracks.Any(track => track.Id == row.OwnerId))
            {
                throw new KeyNotFoundException("字幕轨道不存在。");
            }
            var subtitles = source.Subtitles.Where(line => line.TrackId == row.OwnerId).Select(line => line.Id).ToHashSet();
            return [.. Flatten(source.Layers).Where(layer => layer.SubtitleId is { } id && subtitles.Contains(id)).Select(layer => layer.Id)];
        }
        if (row.Scope == TimelineRowScope.SCENE_LAYER)
        {
            if (!Flatten(source.Layers).Any(layer => layer.Id == row.OwnerId))
            {
                throw new KeyNotFoundException("图层不存在。");
            }
            return [row.OwnerId];
        }
        throw new ArgumentOutOfRangeException(nameof(row));
    }

    private void ClearTimelineAnimationTracks(IReadOnlyCollection<Guid> layerIds, AnimationProperty? property)
    {
        ViewModel.CancelGestures();
        if (property is { } target)
        {
            editor.ClearAnimationTracks(layerIds, target);
        }
        else
        {
            editor.ClearAnimationTracks(layerIds);
        }
        ClearKeyframeSelection();
    }

    internal Task CreateTimelineSubtitleAsync(Guid trackId, MediaTime time, ProjectDocument expected) =>
        RunCommandAsync(async () =>
        {
            if (!TimelineContextIsCurrent(expected) || projectBusy || !TryCommitDrafts())
            {
                return;
            }

            var start = time < MediaTime.Zero ? MediaTime.Zero : time;
            InvalidateTimingSession();
            await BeginTimingCueAsync(trackId, start);
        });

    internal void ClearTimelineClipboard() => timelineClipboard = null;

    private bool TimelineContextIsCurrent(ProjectDocument? expected) => !closing && !projectBusy &&
        (expected is null || ReferenceEquals(expected, editor.Snapshot));
}
