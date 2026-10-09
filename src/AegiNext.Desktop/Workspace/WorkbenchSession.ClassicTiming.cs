using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal Task CommitClassicTimingAsync(TimelineClassicTimingEventArgs value) => RunCommandAsync(() => EditAsync(() =>
    {
        if (!preferences.TimelineClassicTimingEnabled || SelectedCue?.Id != value.CueId)
        {
            return;
        }

        var cue = editor.Snapshot.Subtitles.Single(line => line.Id == value.CueId);
        var start = value.IsStart ? value.Time : cue.Start;
        var end = value.IsStart ? cue.End : value.Time;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (start < MediaTime.Zero || start >= end)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.TimelineTimingInvalid"));
        }

        if (ClipIndex.GetTrackClips(ClipIndex.GetSubtitleTrackId(cue.Id)).Any(clip => clip.SubtitleId != cue.Id &&
            start < clip.End && clip.Start < end))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.TimelineClipCollision"));
        }

        if (cue.Start != start || cue.End != end)
        {
            editor.SetSubtitleTiming(cue.Id, start, end, TimelineEditMode.CROP);
        }
    }));
}
