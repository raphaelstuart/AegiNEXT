using AegiNext.Desktop.Controls;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelViewModel
{
    public RelayCommand ExpandAllTracksCommand { get; }
    public RelayCommand CollapseAllTracksCommand { get; }

    private bool CanExpandAllTracks => !session.IsClosing && !session.IsProjectBusy &&
        !TimelineViewState.CollapsedTrackIds.IsEmpty;
    private bool CanCollapseAllTracks => !session.IsClosing && !session.IsProjectBusy &&
        session.GetTimelineTrackIds().Any(id => !TimelineViewState.CollapsedTrackIds.Contains(id));

    internal void SetTrackCollapsed(TimelineTrackCollapseEventArgs request) =>
        session.SetTimelineTrackCollapsed(request.Id, request.IsCollapsed);

    private void RefreshTrackCollapseCommands()
    {
        ExpandAllTracksCommand.NotifyCanExecuteChanged();
        CollapseAllTracksCommand.NotifyCanExecuteChanged();
    }
}
