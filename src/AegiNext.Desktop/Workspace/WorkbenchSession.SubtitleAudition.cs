using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool CanAuditionSubtitle => !closing && !projectBusy && playback.PendingPosition is null &&
        SelectedCue is { } cue && SelectedLayer?.SubtitleId == cue.Id &&
        controller.Snapshot.AudioAvailable && controller.Snapshot.AudioError is null && controller.Snapshot.Error is null &&
        controller.Snapshot.State is VideoPlaybackState.PAUSED or VideoPlaybackState.PLAYING or VideoPlaybackState.ENDED;

    internal async Task PlaySubtitleAuditionAsync(WorkbenchCommand command)
    {
        if (!CanAuditionSubtitle || !TryCommitDrafts(false) || SelectedCue is not { } cue ||
            controller.MediaInfo is not { } media || SubtitleAuditionRange.Resolve(cue, media, command) is not { } range)
        {
            return;
        }

        ViewModel.CancelGestures();
        await controller.PlayAudioRangeAsync(range.Start, range.End);
    }
}
