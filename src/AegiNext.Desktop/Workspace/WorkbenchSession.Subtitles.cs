using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void AddCue()
    {
        var start = ProjectPosition < MediaTime.Zero ? MediaTime.Zero : ProjectPosition;
        var id = editor.AddSubtitle(start, start + new MediaTime(2), string.Empty);
        SelectCue(id);
    }

    internal void SetCueStart()
    {
        var start = ProjectPosition < MediaTime.Zero ? MediaTime.Zero : ProjectPosition;
        var entered = timingSession.Enter(start);
        if (entered.IsRepeated)
        {
            return;
        }

        editor.AddSubtitles([
            new()
            {
                Id = entered.CueId, Start = entered.Start, End = entered.Start + new MediaTime(2),
                Text = string.Empty
            }
        ]);

        SelectCue(entered.CueId);
        SubtitleScrollRequested?.Invoke(this, EventArgs.Empty);
        timingSession = entered.Session;
        ViewModel.RefreshCommands();
    }

    internal void SetCueEnd()
    {
        if (timingSession.Exit(ProjectPosition) is not { } exited)
        {
            return;
        }

        editor.SetSubtitleTiming(exited.CueId, exited.Start, exited.End, TimelineEditMode.CROP);
        timingSession = exited.Session;
        SelectCue(exited.CueId);
        ViewModel.RefreshCommands();
    }

    internal void SplitCue()
    {
        var cue = SelectedCue ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var boundaries = StringInfo.ParseCombiningCharacters(cue.Text).Append(cue.Text.Length).ToArray();
        if (!textCarets.TryGetValue(cue.Id, out var caret))
        {
            throw new InvalidOperationException(WorkbenchText.Get("SplitCaret"));
        }

        var offset = boundaries.MinBy(value => Math.Abs(value - caret));


        editor.Apply("Split subtitle",
            document => ProjectEditingOperations.SplitSubtitle(document, cue.Id, ProjectPosition, offset));
    }

    internal void MergeCue()
    {
        var cue = SelectedCue ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var index = editor.Snapshot.Subtitles.IndexOf(cue);
        if (index + 1 >= editor.Snapshot.Subtitles.Length)
        {
            return;
        }

        var next = editor.Snapshot.Subtitles[index + 1];
        editor.Apply("Merge subtitles", document => ProjectEditingOperations.MergeSubtitles(document, cue.Id, next.Id));
    }

}
