using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelViewModel
{
    private Guid? soloTrackId;
    private Guid? soloDocumentId;
    private Guid? soloSelectionCueId;
    private Guid? soloSelectionLayerId;
    private HashSet<Guid> soloSelectionIds = [];

    public Guid? SoloTrackId
    {
        get => soloTrackId;
        private set => SetProperty(ref soloTrackId, value);
    }

    /// <summary>切换单轨时间线投影，保留工程和当前片段选择。</summary>
    public void ToggleTrackSolo(Guid trackId)
    {
        if (session.IsClosing || session.IsProjectBusy || !Document.Tracks.Any(track => track.Id == trackId))
        {
            return;
        }

        if (SoloTrackId == trackId)
        {
            ClearTrackSolo();
            return;
        }

        soloDocumentId = Document.Id;
        CaptureTrackSoloSelection();
        SoloTrackId = trackId;
    }

    internal void ClearTrackSolo()
    {
        soloDocumentId = null;
        soloSelectionCueId = null;
        soloSelectionLayerId = null;
        soloSelectionIds.Clear();
        SoloTrackId = null;
    }

    internal void ClearTrackSoloForTrackSelection(Guid trackId)
    {
        if (SoloTrackId is { } solo && solo != trackId)
        {
            ClearTrackSolo();
        }
    }

    internal void ValidateTrackSoloDocument(ProjectDocument value)
    {
        if (SoloTrackId is { } solo && (soloDocumentId != value.Id || !value.Tracks.Any(track => track.Id == solo)))
        {
            ClearTrackSolo();
        }
    }

    internal void ValidateTrackSoloSelection()
    {
        if (SoloTrackId is not { } solo || soloSelectionCueId == session.SelectedCueId &&
            soloSelectionLayerId == session.SelectedLayerId && soloSelectionIds.SetEquals(session.ViewModel.Effects.SelectedIds))
        {
            return;
        }

        CaptureTrackSoloSelection();
        var selected = soloSelectionIds.ToHashSet();
        if (session.SelectedLayerId is { } primary)
        {
            selected.Add(primary);
        }
        if (session.SelectedCueId is { } cue)
        {
            selected.Add(session.ClipIndex.GetSubtitleClip(cue).Id);
        }
        if (Document.Layers.Any(clip => selected.Contains(clip.Id) && clip.TrackId != solo))
        {
            ClearTrackSolo();
        }
    }

    private void CaptureTrackSoloSelection()
    {
        soloSelectionCueId = session.SelectedCueId;
        soloSelectionLayerId = session.SelectedLayerId;
        soloSelectionIds = session.ViewModel.Effects.SelectedIds.ToHashSet();
    }
}
