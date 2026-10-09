using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTrackDeletionTests
{
    [Fact]
    public void PopulatedTrackDeletionRemovesAllClipKindsAndRestoresEverythingInOneUndo()
    {
        var track = new ProjectTrack { Name = "Keep" };
        var first = new SubtitleLine { Start = new(0), End = new(2), Text = "Remove first" };
        var second = new SubtitleLine { Start = new(2), End = new(4), Text = "Remove second" };
        var keep = new SubtitleLine { Start = new(0), End = new(4), Text = "Keep" };
        var keepLayer = SubtitleLayer(keep) with { TrackId = track.Id };
        var shape = new ProjectLayer { TrackId = track.Id, Start = new(4), Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 30, 20) };
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/shared.png");
        var removedShape = shape with { Id = Guid.NewGuid(), TrackId = ProjectTrack.DEFAULT_TRACK_ID };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, track],
            Subtitles = [first, second, keep],
            Layers = [SubtitleLayer(first), SubtitleLayer(second), keepLayer, shape, removedShape],
            Assets = [imageAsset]
        };
        var editor = new ProjectEditor(document);
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.RemoveTrack(ProjectTrack.DEFAULT_TRACK_ID);

        Assert.Equal(1, changes);
        Assert.Same(track, Assert.Single(editor.Snapshot.Tracks));
        Assert.Same(keep, Assert.Single(editor.Snapshot.Subtitles));
        Assert.Equal(document.Assets, editor.Snapshot.Assets);
        Assert.Equal(2, editor.Snapshot.Layers.Length);
        Assert.Same(keepLayer, editor.Snapshot.Layers[0]);
        Assert.Same(shape, editor.Snapshot.Layers[1]);
        var after = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastTrackDeletionRoundTripsWithoutRecreatingATrackAndCanRecover(bool populated)
    {
        var line = new SubtitleLine { Start = new(0), End = new(2), Text = "Remove" };
        var shape = new ProjectLayer { Start = new(2), Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 20, 20) };
        var editor = new ProjectEditor(new()
        {
            Subtitles = populated ? [line] : [],
            Layers = populated ? [SubtitleLayer(line), shape] : [shape]
        });
        var before = editor.Snapshot;

        editor.RemoveTrack(ProjectTrack.DEFAULT_TRACK_ID);

        Assert.Empty(editor.Snapshot.Tracks);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Empty(editor.Snapshot.Layers);
        var after = editor.Snapshot;
        var bytes = ProjectStore.Serialize(after);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Empty(restored.Tracks);
        Assert.Empty(restored.Subtitles);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);

        var reopened = new ProjectEditor(restored);
        var newTrack = reopened.AddTrack("Recovered");
        var cueId = reopened.AddSubtitle(new(0), new(2), "New subtitle");
        Assert.Equal(newTrack, Assert.Single(reopened.Snapshot.Tracks).Id);
        Assert.Equal(newTrack, Assert.Single(reopened.Snapshot.Layers).TrackId);
        Assert.Equal(cueId, reopened.Snapshot.Layers[0].SubtitleId);
    }

    [Fact]
    public void CreatingSubtitleWithoutATrackIsRejectedWithoutChangingHistory()
    {
        var document = new ProjectDocument { Tracks = [] };
        var editor = new ProjectEditor(document);

        Assert.Throws<InvalidOperationException>(() => editor.AddSubtitle(new(0), new(2), "No track"));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void MissingTrackDeletionPreservesSnapshotAndRedo()
    {
        var editor = new ProjectEditor();
        editor.AddTrack("Second");
        Assert.True(editor.Undo());
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() => editor.RemoveTrack(Guid.NewGuid()));

        Assert.Same(before, editor.Snapshot);
        Assert.Equal(0, changes);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void ZeroTracksStillRequiresTheSerializedTrackFieldAndRejectsOrphanSubtitles()
    {
        var document = new ProjectDocument { Tracks = [] };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root.Remove("tracks");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        var line = new SubtitleLine { Text = "Orphan" };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with
        {
            Subtitles = [line], Layers = [SubtitleLayer(line)]
        }));
    }

    private static ProjectLayer SubtitleLayer(SubtitleLine line)
    {
        return new()
        {
            Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Mask = new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(10, 10) },
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
        };
    }
}
