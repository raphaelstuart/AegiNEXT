using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ClipMaskEditingTests
{
    [Fact]
    public void SetAndClearNestedSubtitleMaskAreSingleTransactionsAndPresetsPreserveMask()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "masked");
        var document = initial.Snapshot with { Layers = [new() { Children = initial.Snapshot.Layers }] };
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        var mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) };
        editor.SetClipMask(id, mask);
        Assert.Same(mask, editor.Snapshot.Layers[0].Children[0].Mask);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        editor.ApplyPreset(id, new(Guid.NewGuid(), "opacity", [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]));
        Assert.Same(mask, editor.Snapshot.Layers[0].Children[0].Mask);
        var beforeClear = editor.Snapshot;
        editor.ClearClipMask(id);
        Assert.Null(editor.Snapshot.Layers[0].Children[0].Mask);
        Assert.True(editor.Undo());
        Assert.Same(beforeClear, editor.Snapshot);
    }

    [Fact]
    public void MaskEditRejectsInvalidTargetsAndGenericLayerEditCannotBypassValidation()
    {
        var layer = new ProjectLayer();
        var editor = new ProjectEditor(new() { Layers = [layer] });
        var original = editor.Snapshot;
        var mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) };
        Assert.Throws<InvalidDataException>(() => editor.SetClipMask(layer.Id, mask));
        Assert.Throws<InvalidDataException>(() => editor.ClearClipMask(layer.Id));
        Assert.Throws<InvalidDataException>(() => editor.UpdateLayer(layer.Id, value => value with { Mask = mask }));
        Assert.Throws<KeyNotFoundException>(() => editor.SetClipMask(Guid.NewGuid(), mask));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void EqualMaskEditPreservesSavedSnapshotAndRedo()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(2), "masked");
        var mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) };
        initial.SetClipMask(id, mask);
        var document = initial.Snapshot;
        var editor = new ProjectEditor(document);
        editor.ClearClipMask(id);
        Assert.True(editor.Undo());
        editor.SetClipMask(id, mask with { });
        Assert.Same(document, editor.Snapshot);
        Assert.True(editor.CanRedo);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void SplitPreservesMaskIdentityAndMaskedClipsCannotSilentlyMerge()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "abcd");
        var node = new MaskNode { Position = new(20, 30), OutHandle = new(4, 5) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] };
        editor.SetClipMask(id, mask);
        var beforeSplit = editor.Snapshot;

        editor.Apply("Split", document => ProjectEditingOperations.SplitSubtitle(document, id, new(2), 2));

        Assert.Equal(2, editor.Snapshot.Layers.Length);
        Assert.All(editor.Snapshot.Layers, layer => Assert.Same(mask, layer.Mask));
        var split = editor.Snapshot;
        var secondId = split.Subtitles[1].Id;
        Assert.Throws<InvalidOperationException>(() => editor.Apply("Merge", document =>
            ProjectEditingOperations.MergeSubtitles(document, id, secondId, "")));
        Assert.Same(split, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(beforeSplit, editor.Snapshot);
    }
}
