using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectEditorPersistenceTests
{
    [Fact]
    public void NormalizedSavePreservesUndoLabelsAndSavedSnapshotIdentity()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = CreateEditor(directory.Path);
        editor.AddSubtitle(new(0), new(1), "first");
        var original = editor.Snapshot;
        var persisted = ProjectResources.NormalizeMediaReferences(original, directory.Path);
        var label = editor.UndoLabel;

        editor.MarkSaved(original, persisted);

        Assert.Same(persisted, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(label, editor.UndoLabel);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(persisted, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void SavingOlderNormalizedSnapshotPreservesNewerEditsAndTheirUndoSavePoint()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = CreateEditor(directory.Path);
        editor.AddSubtitle(new(0), new(1), "first");
        var original = editor.Snapshot;
        var persisted = ProjectResources.NormalizeMediaReferences(original, directory.Path);
        editor.AddSubtitle(new(1), new(2), "second");
        var newer = editor.Snapshot;

        editor.MarkSaved(original, persisted);

        Assert.Same(newer, editor.Snapshot);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(editor.Undo());
        Assert.Same(persisted, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.True(editor.Redo());
        Assert.Same(newer, editor.Snapshot);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public void NormalizedSavePreservesExistingRedoAndUpdatesItsReturnSavePoint()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = CreateEditor(directory.Path);
        editor.AddSubtitle(new(0), new(1), "first");
        editor.AddSubtitle(new(1), new(2), "second");
        Assert.True(editor.Undo());
        var original = editor.Snapshot;
        var persisted = ProjectResources.NormalizeMediaReferences(original, directory.Path);
        var redoLabel = editor.RedoLabel;

        editor.MarkSaved(original, persisted);

        Assert.Equal(redoLabel, editor.RedoLabel);
        Assert.True(editor.Redo());
        Assert.Equal(2, editor.Snapshot.Subtitles.Length);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(editor.Undo());
        Assert.Same(persisted, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void InvalidPersistedSnapshotCannotChangeEditorOrHistory()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = CreateEditor(directory.Path);
        editor.AddSubtitle(new(0), new(1), "first");
        var original = editor.Snapshot;
        var label = editor.UndoLabel;

        Assert.Throws<InvalidDataException>(() => editor.MarkSaved(original, original with { Width = 0 }));

        Assert.Same(original, editor.Snapshot);
        Assert.Equal(label, editor.UndoLabel);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Snapshot.Subtitles);
    }

    private static ProjectEditor CreateEditor(string directory)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(directory, "video.mkv"));
        return new(new ProjectDocument { Assets = [asset] });
    }
}
