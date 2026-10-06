using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectEditorNotificationTests
{
    [Fact]
    public void DocumentTransactionsAndSavePointsPublishOneTypedAndLegacyNotificationEach()
    {
        var editor = new ProjectEditor();
        var kinds = new List<ProjectEditorChangeKind>();
        var legacyCount = 0;
        editor.StateChanged += (_, args) => kinds.Add(args.Kind);
        editor.Changed += (_, _) => legacyCount++;

        editor.AddSubtitle(new(0), new(1), "first");
        editor.MarkSaved();
        Assert.True(editor.Undo());
        Assert.True(editor.Redo());
        editor.MarkSaved(editor.Snapshot, editor.Snapshot);
        editor.Reset(new());

        Assert.Equal(
        [
            ProjectEditorChangeKind.DOCUMENT,
            ProjectEditorChangeKind.SAVE_POINT,
            ProjectEditorChangeKind.DOCUMENT,
            ProjectEditorChangeKind.DOCUMENT,
            ProjectEditorChangeKind.SAVE_POINT,
            ProjectEditorChangeKind.DOCUMENT
        ], kinds);
        Assert.Equal(kinds.Count, legacyCount);
    }

    [Fact]
    public void NoOpAndRejectedTransactionsPublishNoNotifications()
    {
        var editor = new ProjectEditor();
        var typedCount = 0;
        var legacyCount = 0;
        editor.StateChanged += (_, _) => typedCount++;
        editor.Changed += (_, _) => legacyCount++;

        editor.Apply("No-op", document => document);
        Assert.False(editor.Undo());
        Assert.False(editor.Redo());
        Assert.Throws<InvalidDataException>(() => editor.Apply("Invalid", document => document with { Width = 0 }));
        Assert.Throws<InvalidDataException>(() => editor.MarkSaved(editor.Snapshot, editor.Snapshot with { Width = 0 }));

        Assert.Equal(0, typedCount);
        Assert.Equal(0, legacyCount);
    }

    [Fact]
    public void NotificationsRunOutsideTheEditorLock()
    {
        var editor = new ProjectEditor();
        var workerCompleted = false;
        editor.StateChanged += (_, _) =>
        {
            workerCompleted = Task.Run(() => editor.Snapshot).Wait(TimeSpan.FromSeconds(5));
        };

        editor.Reset(new ProjectDocument());

        Assert.True(workerCompleted);
    }
}
