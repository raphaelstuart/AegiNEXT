using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool TrySelectSubtitleColorTagFilter(SubtitleColorTagFilter filter)
    {
        if (closing || IsUpdating || IsProjectBusy || !TryCommitDrafts())
        {
            return false;
        }

        if (!filter.IsAll && filter.TagId is { } id && !editor.Snapshot.ColorTags.Any(tag => tag.Id == id))
        {
            return false;
        }

        using var update = BeginWorkbenchUpdate();
        ViewModel.CancelGestures();
        ViewModel.Subtitles.AcceptColorTagFilter(filter);
        RefreshDocument();
        ViewModel.RefreshCommands();
        return true;
    }

    internal Task SetSubtitleColorTagAsync(IReadOnlyCollection<Guid> ids, Guid? tagId, ProjectDocument? expected = null)
    {
        var targets = ids.ToArray();
        return RunCommandAsync(() => ColorTagContextIsCurrent(expected)
            ? EditAsync(() => editor.SetSubtitleColorTag(targets, tagId)) : Task.CompletedTask);
    }

    internal Task ApplySubtitleColorTagAsync(IReadOnlyCollection<Guid> ids, SubtitleColorTag template,
        ProjectDocument? expected = null)
    {
        var targets = ids.ToArray();
        return RunCommandAsync(() => ColorTagContextIsCurrent(expected)
            ? EditAsync(() => editor.ApplySubtitleColorTag(targets, template)) : Task.CompletedTask);
    }

    internal Task OpenSubtitleColorTagSettingsAsync() => ViewModel.RequestHostCommandAsync(
        Shortcuts.WorkbenchCommand.OPEN_SETTINGS, SettingsPage.SUBTITLE_COLOR_TAGS);

    internal bool CanMergeVisibleSubtitleSelection => !closing && !IsProjectBusy &&
        VisibleSubtitleMergeTargets().Length >= 2;

    internal Task MergeVisibleSubtitleSelectionAsync() => RunCommandAsync(() => EditAsync(() =>
    {
        var targets = VisibleSubtitleMergeTargets();
        if (targets.Length >= 2)
        {
            MergeCue(targets);
        }
    }));

    private Guid[] VisibleSubtitleMergeTargets()
    {
        var targets = MergeSubtitleTargets();
        return targets.Length >= 2 && targets.All(id => ViewModel.Subtitles.IsRowVisible(id)) ? targets : [];
    }

    private bool ColorTagContextIsCurrent(ProjectDocument? expected) => !closing && !IsProjectBusy &&
        (expected is null || ReferenceEquals(expected, editor.Snapshot));

    private bool RevealSubtitleColorTagTargets(IEnumerable<Guid> ids)
    {
        var filter = ViewModel.Subtitles.ColorTagFilter;
        if (filter.IsAll)
        {
            return false;
        }

        var targets = ids.ToHashSet();
        if (editor.Snapshot.Subtitles.Any(line => targets.Contains(line.Id) && !filter.Matches(line)))
        {
            ViewModel.Subtitles.AcceptColorTagFilter(SubtitleColorTagFilter.All);
            return true;
        }
        return false;
    }
}
