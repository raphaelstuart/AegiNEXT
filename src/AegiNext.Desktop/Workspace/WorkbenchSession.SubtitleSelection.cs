using System.Collections.Immutable;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ImmutableArray<Guid> selectedSubtitleIds = [];
    private Guid? subtitleSelectionPrimaryId;

    internal IReadOnlyList<Guid> SelectedSubtitleIds => selectedSubtitleIds;

    internal bool SelectSubtitleRows(Guid? primaryId, IEnumerable<Guid> ids)
    {
        if (IsUpdating || IsProjectBusy || closing)
        {
            return false;
        }

        var requested = ids.ToHashSet();
        var selected = editor.Snapshot.Subtitles.Where(line => requested.Contains(line.Id) && line.TrackId == CurrentTrackId)
            .OrderBy(line => line.Start).Select(line => line.Id).ToImmutableArray();
        primaryId = primaryId is { } candidate && selected.Contains(candidate) ? candidate : selected.IsEmpty ? null : selected[0];
        if (primaryId == SelectedCueId && selectedSubtitleIds.SequenceEqual(selected))
        {
            CenterSubtitleSelection();
            return true;
        }

        if (!TryCommitDrafts())
        {
            RefreshDocument();
            return false;
        }

        ViewModel.CancelGestures();
        InvalidateTimingSession();
        selectedSubtitleIds = selected;
        subtitleSelectionPrimaryId = primaryId;
        SelectedCueId = primaryId;
        var layers = Flatten(editor.Snapshot.Layers).ToArray();
        SelectedLayerId = layers.FirstOrDefault(layer => layer.SubtitleId == primaryId && primaryId is not null)?.Id;
        ViewModel.Effects.SelectedIds = layers.Where(layer => layer.SubtitleId is { } id && selected.Contains(id))
            .Select(layer => layer.Id).ToArray();
        SelectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        RefreshDocument();
        CenterSubtitleSelection();
        return true;
    }

    private void CenterSubtitleSelection()
    {
        if (SelectedCueId is { } cueId)
        {
            ViewModel.Timeline.CenterSubtitle(cueId);
        }
        else
        {
            ViewModel.Timeline.ResumePlaybackFollow();
        }
    }

    internal void FocusSubtitleRow(Guid id)
    {
        SelectSubtitleRows(id, selectedSubtitleIds.Contains(id) ? selectedSubtitleIds : [id]);
    }

    private void ResetSubtitleSelection(Guid? primaryId = null)
    {
        selectedSubtitleIds = primaryId is { } id ? [id] : [];
        subtitleSelectionPrimaryId = primaryId;
    }

    private void RefreshSubtitleSelection()
    {
        if (SelectedCueId != subtitleSelectionPrimaryId)
        {
            ResetSubtitleSelection(SelectedCueId);
        }

        var surviving = editor.Snapshot.Subtitles.Where(line => line.TrackId == CurrentTrackId && selectedSubtitleIds.Contains(line.Id))
            .OrderBy(line => line.Start).Select(line => line.Id).ToImmutableArray();
        if (SelectedCueId is { } primary && !surviving.Contains(primary))
        {
            surviving = [primary];
        }

        selectedSubtitleIds = surviving;
        ViewModel.Subtitles.NotifySelectionChanged();
    }

    private void SynchronizeSubtitleSelectionFromLayers()
    {
        SelectedCueId = SelectedLayer?.SubtitleId;
        if (SelectedCue is not { } primary)
        {
            ResetSubtitleSelection();
            return;
        }

        var selectedLayers = ViewModel.Effects.SelectedIds.ToHashSet();
        selectedLayers.Add(SelectedLayer!.Id);
        var selected = Flatten(editor.Snapshot.Layers).Where(layer => selectedLayers.Contains(layer.Id))
            .Where(layer => layer.SubtitleId is not null).Select(layer => layer.SubtitleId!.Value).ToHashSet();
        selectedSubtitleIds = editor.Snapshot.Subtitles.Where(line => line.TrackId == primary.TrackId && selected.Contains(line.Id))
            .OrderBy(line => line.Start).Select(line => line.Id).ToImmutableArray();
        subtitleSelectionPrimaryId = primary.Id;
    }

    private bool CanMergeSubtitleSelection()
    {
        return SelectedCue is { } cue && (selectedSubtitleIds.Length > 1 || editor.Snapshot.Subtitles.Any(line =>
            line.TrackId == cue.TrackId && line.Start > cue.Start));
    }

    private Guid[] MergeSubtitleTargets()
    {
        if (SelectedCue is not { } cue)
        {
            return [];
        }

        if (selectedSubtitleIds.Length > 1)
        {
            return selectedSubtitleIds.ToArray();
        }

        var next = editor.Snapshot.Subtitles.Where(line => line.TrackId == cue.TrackId && line.Start > cue.Start)
            .OrderBy(line => line.Start).FirstOrDefault();
        return next is null ? [] : [cue.Id, next.Id];
    }
}
