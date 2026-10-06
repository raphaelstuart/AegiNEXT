using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private TimelineViewState timelineViewState = new();
    private TimelineViewState savedTimelineViewState = new();

    internal TimelineViewState TimelineViewState => timelineViewState;
    internal bool HasUnsavedChanges => editor.HasUnsavedChanges ||
        !timelineViewState.CollapsedAnimationRows.SequenceEqual(savedTimelineViewState.CollapsedAnimationRows);

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

        var next = NormalizeTimelineViewState(new()
        {
            CollapsedAnimationRows = isCollapsed ? rows.Add(id) : rows.Remove(id)
        });
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
        return contentSnapshot.TimelineViewState.CollapsedAnimationRows.SequenceEqual(timelineViewState.CollapsedAnimationRows)
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
        return state.CollapsedAnimationRows.SequenceEqual(sorted)
            ? state : state with { CollapsedAnimationRows = [.. sorted] };
    }
}
