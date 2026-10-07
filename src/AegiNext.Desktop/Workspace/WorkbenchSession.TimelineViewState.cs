using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private TimelineViewState timelineViewState = new();
    private TimelineViewState savedTimelineViewState = new();

    internal TimelineViewState TimelineViewState => timelineViewState;
    internal bool HasUnsavedChanges => editor.HasUnsavedChanges ||
        !TimelineViewStatesEqual(timelineViewState, savedTimelineViewState);

    internal void SetTimelineAnimationRowCollapsed(TimelineAnimationRowId id, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (closing || projectBusy)
        {
            return;
        }

        var rows = timelineViewState.CollapsedAnimationRows;
        if (rows.Contains(id) == isCollapsed)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            CollapsedAnimationRows = isCollapsed ? rows.Add(id) : rows.Remove(id)
        });
    }

    internal void SetTimelineTrackCollapsed(Guid id, bool isCollapsed)
    {
        if (closing || projectBusy || id == Guid.Empty || !GetTimelineTrackIds().Contains(id))
        {
            return;
        }

        var ids = timelineViewState.CollapsedTrackIds;
        if (ids.Contains(id) == isCollapsed)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            CollapsedTrackIds = isCollapsed ? ids.Add(id) : ids.Remove(id)
        });
    }

    internal void SetAllTimelineTracksCollapsed(bool isCollapsed)
    {
        if (closing || projectBusy)
        {
            return;
        }

        ApplyTimelineViewState(timelineViewState with
        {
            CollapsedTrackIds = isCollapsed
                ? [.. timelineViewState.CollapsedTrackIds.Union(GetTimelineTrackIds())] : []
        });
    }

    internal IEnumerable<Guid> GetTimelineTrackIds() => DocumentSnapshot.SubtitleTracks.Select(track => track.Id)
        .Concat(EnumerateTimelineSceneTrackIds(DocumentSnapshot.Layers)).Distinct();

    private static IEnumerable<Guid> EnumerateTimelineSceneTrackIds(IEnumerable<ProjectLayer> layers)
    {
        foreach (var layer in layers)
        {
            if (layer.Kind != LayerKind.SUBTITLE)
            {
                yield return layer.Id;
            }

            foreach (var id in EnumerateTimelineSceneTrackIds(layer.Children))
            {
                yield return id;
            }
        }
    }

    private void ApplyTimelineViewState(TimelineViewState state)
    {
        var next = NormalizeTimelineViewState(state);
        if (TimelineViewStatesEqual(timelineViewState, next))
        {
            return;
        }

        timelineViewState = next;
        ViewModel.Timeline.TimelineViewState = next;
        RefreshTitle();
        ViewModel.RefreshCommands();
    }

    internal void ResetTimelineViewState(ProjectDocument document)
    {
        var state = NormalizeTimelineViewState(document.TimelineViewState);
        timelineViewState = state;
        savedTimelineViewState = state;
        ViewModel.Timeline.TimelineViewState = state;
    }

    internal ProjectDocument CreatePersistenceSnapshot(ProjectDocument contentSnapshot)
    {
        return TimelineViewStatesEqual(contentSnapshot.TimelineViewState, timelineViewState)
            ? contentSnapshot : contentSnapshot with { TimelineViewState = timelineViewState };
    }

    internal void AcceptProjectSave(ProjectDocument contentSnapshot, ProjectDocument persistedSnapshot, bool resetContent = false)
    {
        var savedState = NormalizeTimelineViewState(persistedSnapshot.TimelineViewState);
        var persistedContent = persistedSnapshot with { TimelineViewState = contentSnapshot.TimelineViewState };
        if (persistedContent == contentSnapshot)
        {
            persistedContent = contentSnapshot;
        }

        if (resetContent)
        {
            editor.Reset(persistedContent);
        }
        else
        {
            editor.MarkSaved(contentSnapshot, persistedContent);
        }

        savedTimelineViewState = savedState;
        RefreshTitle();
        ViewModel.RefreshCommands();
    }

    private static TimelineViewState NormalizeTimelineViewState(TimelineViewState state)
    {
        state.Validate();
        var sorted = state.CollapsedAnimationRows.OrderBy(row => row.Scope).ThenBy(row => row.OwnerId)
            .ThenBy(row => row.Property).ToArray();
        var sortedTracks = state.CollapsedTrackIds.Order().ToArray();
        return state.CollapsedAnimationRows.SequenceEqual(sorted) && state.CollapsedTrackIds.SequenceEqual(sortedTracks)
            ? state : state with { CollapsedAnimationRows = [.. sorted], CollapsedTrackIds = [.. sortedTracks] };
    }

    private static bool TimelineViewStatesEqual(TimelineViewState first, TimelineViewState second) =>
        first.CollapsedAnimationRows.SequenceEqual(second.CollapsedAnimationRows) &&
        first.CollapsedTrackIds.SequenceEqual(second.CollapsedTrackIds);
}
