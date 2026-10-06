using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTrackDeletionTests
{
    [Fact]
    public void PopulatedTrackDeletionRemovesNestedClipsAndRestoresEverythingInOneUndo()
    {
        var track = new SubtitleTrack { Name = "Keep" };
        var first = new SubtitleLine { Start = new(0), End = new(2), Text = "Remove first" };
        var second = new SubtitleLine { Start = new(2), End = new(4), Text = "Remove second" };
        var keep = new SubtitleLine { Start = new(0), End = new(4), Text = "Keep", TrackId = track.Id };
        var keepLayer = SubtitleLayer(keep);
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 30, 20) };
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/shared.png");
        var nested = new ProjectLayer { Children = [SubtitleLayer(second), keepLayer, shape] };
        var group = new ProjectLayer { Children = [SubtitleLayer(first), nested] };
        var document = new ProjectDocument
        {
            SubtitleTracks = [SubtitleTrack.Default, track],
            Subtitles = [first, second, keep],
            Layers = [group],
            Assets = [imageAsset]
        };
        var editor = new ProjectEditor(document);
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.RemoveSubtitleTrack(SubtitleTrack.DEFAULT_TRACK_ID);

        Assert.Equal(1, changes);
        Assert.Same(track, Assert.Single(editor.Snapshot.SubtitleTracks));
        Assert.Same(keep, Assert.Single(editor.Snapshot.Subtitles));
        Assert.Equal(document.Assets, editor.Snapshot.Assets);
        var remainingGroup = Assert.Single(editor.Snapshot.Layers);
        Assert.Equal(group.Id, remainingGroup.Id);
        var remainingNested = Assert.Single(remainingGroup.Children);
        Assert.Equal(nested.Id, remainingNested.Id);
        Assert.Equal(2, remainingNested.Children.Length);
        Assert.Same(keepLayer, remainingNested.Children[0]);
        Assert.Same(shape, remainingNested.Children[1]);
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
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.ELLIPSE, 20, 20) };
        var editor = new ProjectEditor(new()
        {
            Subtitles = populated ? [line] : [],
            Layers = populated ? [SubtitleLayer(line), shape] : [shape]
        });
        var before = editor.Snapshot;

        editor.RemoveSubtitleTrack(SubtitleTrack.DEFAULT_TRACK_ID);

        Assert.Empty(editor.Snapshot.SubtitleTracks);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Same(shape, Assert.Single(editor.Snapshot.Layers));
        var after = editor.Snapshot;
        var bytes = ProjectStore.Serialize(after);
        var restored = ProjectStore.Deserialize(bytes);
        Assert.Empty(restored.SubtitleTracks);
        Assert.Empty(restored.Subtitles);
        Assert.Equal(bytes, ProjectStore.Serialize(restored));
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);

        var reopened = new ProjectEditor(restored);
        var newTrack = reopened.AddSubtitleTrack("Recovered");
        var cueId = reopened.AddSubtitle(new(0), new(2), "New subtitle");
        Assert.Equal(newTrack, Assert.Single(reopened.Snapshot.SubtitleTracks).Id);
        Assert.Equal(newTrack, Assert.Single(reopened.Snapshot.Subtitles).TrackId);
        Assert.Equal(cueId, reopened.Snapshot.Layers[1].SubtitleId);
    }

    [Fact]
    public void CreatingSubtitleWithoutATrackIsRejectedWithoutChangingHistory()
    {
        var document = new ProjectDocument { SubtitleTracks = [] };
        var editor = new ProjectEditor(document);

        Assert.Throws<InvalidOperationException>(() => editor.AddSubtitle(new(0), new(2), "No track"));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void MissingTrackDeletionPreservesSnapshotAndRedo()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitleTrack("Second");
        Assert.True(editor.Undo());
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() => editor.RemoveSubtitleTrack(Guid.NewGuid()));

        Assert.Same(before, editor.Snapshot);
        Assert.Equal(0, changes);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void ZeroTracksStillRequiresTheSerializedTrackFieldAndRejectsOrphanSubtitles()
    {
        var document = new ProjectDocument { SubtitleTracks = [] };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root.Remove("subtitleTracks");
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
