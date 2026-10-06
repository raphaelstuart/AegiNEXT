using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid? timingPreviewTrackId;
    private bool timingPreviewFollowing;
    private bool timingPreviewWasPlaying;

    private MediaTime ResolveTimingEnd(Guid cueId, Guid trackId, MediaTime start, MediaTime requested)
    {
        var end = start + new MediaTime(1, 1000);
        if (requested > end)
        {
            end = requested;
        }

        var next = editor.Snapshot.Subtitles.Where(line => line.Id != cueId && line.TrackId == trackId && line.Start > start)
            .OrderBy(line => line.Start).FirstOrDefault();
        if (next is not null && end > next.Start)
        {
            end = next.Start;
        }

        return end;
    }

    private void StartTimingPreview(TimelineTimingPreview preview, Guid trackId)
    {
        timingPreviewTrackId = trackId;
        timingPreviewFollowing = true;
        timingPreviewWasPlaying = controller.Snapshot.State == VideoPlaybackState.PLAYING;
        ViewModel.Timeline.TimingPreview = preview;
    }

    private void ClearTimingPreview()
    {
        timingPreviewFollowing = false;
        timingPreviewWasPlaying = false;
        timingPreviewTrackId = null;
        ViewModel.Timeline.TimingPreview = null;
        timingSession = timingSession.Reset();
    }

    private void UpdateTimingPreview(VideoPlaybackState state, MediaTime position)
    {
        if (!timingPreviewFollowing || ViewModel.Timeline.TimingPreview is not { } preview || timingPreviewTrackId is not { } trackId)
        {
            return;
        }

        if (timingPreviewWasPlaying && state != VideoPlaybackState.PLAYING)
        {
            InvalidateTimingSession();
            return;
        }

        timingPreviewWasPlaying |= state == VideoPlaybackState.PLAYING;
        var end = ResolveTimingEnd(preview.CueId, trackId, preview.Start, position);
        if (preview.End != end)
        {
            ViewModel.Timeline.TimingPreview = preview with { End = end };
        }
    }

    private bool FreezeTimingPreview()
    {
        var preview = ViewModel.Timeline.TimingPreview;
        if (pendingTimingEntry is not null && pendingTimingEnd is null && preview is not null)
        {
            pendingTimingEnd = preview.End;
        }

        ClearTimingPreview();
        if (preview is null || pendingTimingEntry is not null || closing)
        {
            return true;
        }

        try
        {
            CommitTimingPreview(preview);
            return true;
        }
        catch (Exception error)
        {
            ShowError(error);
            return false;
        }
    }

    private void CommitTimingPreview(TimelineTimingPreview preview)
    {
        var cue = editor.Snapshot.Subtitles.FirstOrDefault(line => line.Id == preview.CueId);
        if (cue is not null && cue.Start == preview.Start && cue.End != preview.End)
        {
            editor.SetSubtitleTiming(cue.Id, cue.Start, preview.End, TimelineEditMode.CROP);
        }
    }

    private ProjectDocument OverlayTimingPreview(ProjectDocument document)
    {
        if (ViewModel.Timeline.TimingPreview is not { } preview)
        {
            return document;
        }

        var cue = document.Subtitles.FirstOrDefault(line => line.Id == preview.CueId);
        if (cue is null || cue.Start != preview.Start)
        {
            return document;
        }

        var subtitles = cue.End == preview.End ? document.Subtitles :
            document.Subtitles.SetItem(document.Subtitles.IndexOf(cue), cue with { End = preview.End });
        var layers = OverlayTimingPreviewLayers(document.Layers, preview);
        return subtitles == document.Subtitles && layers == document.Layers ? document :
            document with { Subtitles = subtitles, Layers = layers };
    }

    private static ImmutableArray<ProjectLayer> OverlayTimingPreviewLayers(ImmutableArray<ProjectLayer> layers, TimelineTimingPreview preview)
    {
        var result = layers;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var children = OverlayTimingPreviewLayers(layer.Children, preview);
            var end = layer.SubtitleId == preview.CueId ? preview.End : layer.End;
            if (layer.End != end || children != layer.Children)
            {
                result = result.SetItem(index, layer with { End = end, Children = children });
            }
        }

        return result;
    }

    private async Task BeginTimingCueAsync(Guid trackId, MediaTime start)
    {
        var entered = timingSession.Enter(start);
        if (entered.IsRepeated || pendingTimingEntry is not null)
        {
            return;
        }

        var source = editor.Snapshot;
        if (source.Subtitles.Any(line => line.TrackId == trackId && line.Start <= start && start < line.End))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.TimelineClipCollision"));
        }

        ViewModel.Timeline.ResumePlaybackFollow();
        var initialEnd = ResolveTimingEnd(entered.CueId, trackId, start, start);
        var preview = new TimelineTimingPreview(entered.CueId, start, initialEnd);
        var generation = projectGeneration;
        var presetId = ViewModel.Styles.SelectedPreset?.Id;
        var token = ProjectOperationsToken;
        pendingTimingEntry = entered;
        pendingTimingEnd = null;
        StartTimingPreview(preview, trackId);
        SetProjectBusy(true);
        var created = false;
        MediaTime? finalEnd = null;
        try
        {
            var prepared = await styles.PrepareCreationAsync(trackId, presetId);
            token.ThrowIfCancellationRequested();
            if (closing || generation != projectGeneration || !ReferenceEquals(source, editor.Snapshot))
            {
                return;
            }

            editor.Apply("Create subtitle clips", _ => ProjectEditingOperations.CreateSubtitleClips(prepared.Project,
                [new() { Id = entered.CueId, TrackId = trackId, Start = start, End = initialEnd, Text = string.Empty,
                    StyleName = prepared.StyleName }],
                trackId, prepared.Style));
            created = true;
            preview = ViewModel.Timeline.TimingPreview ?? preview;
            finalEnd = pendingTimingEnd;
        }
        finally
        {
            ClearTimingPreview();
            pendingTimingEntry = null;
            pendingTimingEnd = null;
            SetProjectBusy(false);
        }

        if (!created || closing)
        {
            return;
        }

        SelectCue(entered.CueId);
        SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        if (finalEnd is { } end)
        {
            CommitTimingPreview(preview with { End = end });
        }
        else
        {
            timingSession = entered.Session;
            StartTimingPreview(preview, trackId);
            Tick();
        }

        ViewModel.RefreshCommands();
    }
}
