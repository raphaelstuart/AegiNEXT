using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>接受同目录保存的规范化快照，保留历史与保存点身份，且不覆盖保存期间的较新编辑。</summary>
    public void MarkSaved(ProjectDocument originalSnapshot, ProjectDocument persistedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(originalSnapshot);
        ProjectValidator.Validate(persistedSnapshot);
        lock (gate)
        {
            EnsureNotEditing();
            if (!ReferenceEquals(originalSnapshot, persistedSnapshot))
            {
                if (ReferenceEquals(snapshot, originalSnapshot))
                {
                    snapshot = persistedSnapshot;
                }

                ReplaceHistorySnapshot(undo, originalSnapshot, persistedSnapshot);
                ReplaceHistorySnapshot(redo, originalSnapshot, persistedSnapshot);
            }

            saved = persistedSnapshot;
        }

        NotifyChanged(ProjectEditorChangeKind.SAVE_POINT);
    }

    /// <summary>原子接受跨目录保存，重定位当前内容并保留实际写盘的旧保存点；旧目录历史不再可用。</summary>
    public void AcceptRelocatedSave(ProjectDocument expectedCurrent, ProjectDocument relocatedCurrent,
        ProjectDocument originalSaveSnapshot, ProjectDocument relocatedSaveSnapshot)
    {
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(originalSaveSnapshot);
        ProjectValidator.Validate(relocatedCurrent);
        ProjectValidator.Validate(relocatedSaveSnapshot);
        lock (gate)
        {
            EnsureNotEditing();
            if (!ReferenceEquals(snapshot, expectedCurrent))
            {
                throw new InvalidOperationException("工程在资源重定位期间发生变化。");
            }

            snapshot = ReferenceEquals(expectedCurrent, originalSaveSnapshot) || relocatedCurrent == relocatedSaveSnapshot
                ? relocatedSaveSnapshot : relocatedCurrent;
            saved = relocatedSaveSnapshot;
            undo.Clear();
            redo.Clear();
        }

        NotifyChanged(ProjectEditorChangeKind.RELOCATION);
    }

    private static void ReplaceHistorySnapshot(List<ProjectHistoryEntry> history, ProjectDocument originalSnapshot,
        ProjectDocument persistedSnapshot)
    {
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            var before = ReferenceEquals(entry.Before, originalSnapshot) ? persistedSnapshot : entry.Before;
            var after = ReferenceEquals(entry.After, originalSnapshot) ? persistedSnapshot : entry.After;
            if (!ReferenceEquals(before, entry.Before) || !ReferenceEquals(after, entry.After))
            {
                history[index] = entry with { Before = before, After = after };
            }
        }
    }
}
