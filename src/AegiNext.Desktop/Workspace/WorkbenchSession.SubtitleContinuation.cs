using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal async Task<Guid?> AdvanceSubtitleRowAsync(Guid sourceId)
    {
        if (updatingWorkbench || projectBusy || closing)
        {
            return null;
        }

        var position = ProjectPosition;
        Guid? targetId = null;
        await RunCommandAsync(async () =>
        {
            if (!TryCommitDrafts())
            {
                return;
            }

            var trackId = CurrentTrackId;
            var lines = editor.Snapshot.Subtitles.Where(line => line.TrackId == trackId).OrderBy(line => line.Start).ToArray();
            var index = Array.FindIndex(lines, line => line.Id == sourceId);
            if (index < 0)
            {
                return;
            }

            if (index + 1 < lines.Length)
            {
                var nextId = lines[index + 1].Id;
                if (SelectSubtitleRows(nextId, selectedSubtitleIds.Contains(nextId) ? selectedSubtitleIds : [nextId]))
                {
                    targetId = nextId;
                }
                return;
            }

            var start = lines[index].End;
            if (position <= start)
            {
                throw new InvalidDataException(Localization.Get("Workbench.SubtitleContinuationTimeRequired"));
            }

            var cue = new SubtitleLine { TrackId = trackId, Start = start, End = position, Text = string.Empty };
            var presetId = ViewModel.Styles.SelectedPreset?.Id;
            InvalidateTimingSession();
            try
            {
                SetProjectBusy(true);
                await CreateSubtitleClipsAsync([cue], trackId, presetId);
            }
            finally
            {
                SetProjectBusy(false);
            }

            if (!closing && CurrentTrackId == trackId && SelectSubtitleRows(cue.Id, [cue.Id]))
            {
                targetId = cue.Id;
            }
        });
        return targetId;
    }
}
