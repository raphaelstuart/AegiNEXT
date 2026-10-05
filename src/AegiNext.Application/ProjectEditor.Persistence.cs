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

        Changed?.Invoke(this, EventArgs.Empty);
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
