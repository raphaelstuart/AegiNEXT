using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ClipCrossTrackPasteTests
{
    [Fact]
    public void TargetTrackPreservesCapturedTrackGapsTimingStylesAndGraphicLayersInOneTransaction()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document,
            [primary.Id, document.Layers[4].Id, document.Layers[6].Id], primary.Id);
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;
        var start = new MediaTime(1001, 30);

        var result = editor.PasteClips(content, start, document.SubtitleTracks[2].Id);

        Assert.Equal(1, changes);
        Assert.Same(editor.Snapshot, result.Document);
        Assert.Equal(result.LayerIds[0], result.PrimaryId);
        Assert.Equal(document.SubtitleTracks[1].Id, content.ReferenceTrackId);
        Assert.Equal(document.SubtitleTracks[2].Id, result.Document.Subtitles[5].TrackId);
        Assert.Equal(document.SubtitleTracks[4].Id, result.Document.Subtitles[6].TrackId);
        Assert.Equal(document.Subtitles[1].Style, result.Document.Subtitles[5].Style);
        Assert.Equal(document.Subtitles[3].Style, result.Document.Subtitles[6].Style);
        Assert.Equal(document.Assets, result.Document.Assets);
        Assert.Equal(document.SubtitleTracks, result.Document.SubtitleTracks);
        for (var index = 0; index < content.Layers.Length; index++)
        {
            var source = content.Layers[index];
            var copy = result.Document.Layers[document.Layers.Length + index];
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal(source.Start - content.EarliestStart + start, copy.Start);
            Assert.Equal(source.End - content.EarliestStart + start, copy.End);
        }
        var image = result.Document.Layers[^1];
        Assert.Equal(LayerKind.IMAGE, image.Kind);
        Assert.Null(image.SubtitleId);
        Assert.Equal(document.Layers[6].Image, image.Image);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result.Document, editor.Snapshot);
    }

    [Fact]
    public void ExplicitReferenceCanBeUnselectedAndOverridesThePrimarySubtitleTrack()
    {
        var document = CreateDocument();
        var primary = document.Layers[1];
        var referenceTrackId = document.SubtitleTracks[2].Id;
        var content = ProjectEditingOperations.CaptureClips(document,
            [primary.Id, document.Layers[2].Id], primary.Id, referenceTrackId);

        var result = ProjectEditingOperations.PasteClips(document, content, new(10), document.SubtitleTracks[3].Id);

        Assert.Equal(referenceTrackId, content.ReferenceTrackId);
        Assert.Equal<Guid>(document.SubtitleTracks.Select(track => track.Id), content.SourceTrackIds);
        Assert.Equal(document.SubtitleTracks[1].Id, result.Document.Subtitles[5].TrackId);
        Assert.Equal(document.SubtitleTracks[2].Id, result.Document.Subtitles[6].TrackId);
        Assert.Equal(result.LayerIds[0], result.PrimaryId);
    }

    [Fact]
    public void GraphicPrimaryDefaultsToTheFirstSubtitleInCompositionOrder()
    {
        var document = CreateDocument();
        document = document with { Subtitles = document.Subtitles.Reverse().ToImmutableArray() };
        var shape = document.Layers[0];
        var content = ProjectEditingOperations.CaptureClips(document,
            [document.Layers[4].Id, shape.Id, document.Layers[2].Id], shape.Id);

        var result = ProjectEditingOperations.PasteClips(document, content, new(10), document.SubtitleTracks[2].Id);

        Assert.Equal(document.SubtitleTracks[1].Id, content.ReferenceTrackId);
        var first = result.Document.Subtitles.Single(line => line.Text == "cue 1" && line.Start > new MediaTime(5));
        var second = result.Document.Subtitles.Single(line => line.Text == "cue 3" && line.Start > new MediaTime(5));
        Assert.Equal(document.SubtitleTracks[2].Id, first.TrackId);
        Assert.Equal(document.SubtitleTracks[4].Id, second.TrackId);
        Assert.Equal(result.LayerIds[0], result.PrimaryId);
        Assert.Null(result.Document.Layers.Single(layer => layer.Id == result.PrimaryId).SubtitleId);
    }

    [Fact]
    public void CaptureRejectsAnUnknownExplicitReferenceTrack()
    {
        var document = CreateDocument();
        var shape = document.Layers[0];

        Assert.Throws<KeyNotFoundException>(() =>
            ProjectEditingOperations.CaptureClips(document, [shape.Id], shape.Id, Guid.NewGuid()));
    }

    [Fact]
    public void FrozenSourceOrderSurvivesTrackReordering()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document,
            [primary.Id, document.Layers[4].Id], primary.Id);
        var reordered = document with { SubtitleTracks = document.SubtitleTracks.Reverse().ToImmutableArray() };

        var result = ProjectEditingOperations.PasteClips(reordered, content, new(10), document.SubtitleTracks[2].Id);

        Assert.Equal<Guid>(document.SubtitleTracks.Select(track => track.Id), content.SourceTrackIds);
        Assert.Equal(document.SubtitleTracks[2].Id, result.Document.Subtitles[5].TrackId);
        Assert.Equal(document.SubtitleTracks[0].Id, result.Document.Subtitles[6].TrackId);
        Assert.Equal(document.SubtitleTracks[1].Id, content.Subtitles[0].TrackId);
        Assert.Equal(document.SubtitleTracks[3].Id, content.Subtitles[1].TrackId);
    }

    [Fact]
    public void FrozenSourceTrackCanBeDeletedBeforePastingToAnExistingTarget()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document, [primary.Id], primary.Id);
        var withoutClip = ProjectEditingOperations.RemoveClips(document, [primary.Id]);
        var withoutTrack = ProjectEditingOperations.RemoveSubtitleTrack(withoutClip, document.SubtitleTracks[1].Id);

        var result = ProjectEditingOperations.PasteClips(withoutTrack, content, new(10), document.SubtitleTracks[2].Id);

        Assert.Equal(document.SubtitleTracks[2].Id, result.Document.Subtitles[^1].TrackId);
        Assert.Equal(document.SubtitleTracks[1].Id, content.Subtitles[0].TrackId);
        Assert.Equal<Guid>(document.SubtitleTracks.Select(track => track.Id), content.SourceTrackIds);
        Assert.Throws<InvalidDataException>(() => ProjectEditingOperations.PasteClips(withoutTrack, content, new(10)));
    }

    [Fact]
    public void FrozenUnselectedReferenceTrackCanBeDeletedBeforePasting()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var referenceTrackId = document.SubtitleTracks[2].Id;
        var content = ProjectEditingOperations.CaptureClips(document,
            [primary.Id, document.Layers[4].Id], primary.Id, referenceTrackId);
        var withoutClip = ProjectEditingOperations.RemoveClips(document, [document.Layers[3].Id]);
        var withoutTrack = ProjectEditingOperations.RemoveSubtitleTrack(withoutClip, referenceTrackId);

        var result = ProjectEditingOperations.PasteClips(withoutTrack, content, new(10), document.SubtitleTracks[1].Id);

        Assert.Equal(referenceTrackId, content.ReferenceTrackId);
        Assert.Equal(document.SubtitleTracks[0].Id, result.Document.Subtitles[4].TrackId);
        Assert.Equal(document.SubtitleTracks[3].Id, result.Document.Subtitles[5].TrackId);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 3)]
    public void MappingBeyondEitherTrackBoundaryRejectsTheWholePaste(int referenceIndex, int targetIndex)
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document,
            [document.Layers[0].Id, primary.Id, document.Layers[4].Id], primary.Id,
            document.SubtitleTracks[referenceIndex].Id);
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<InvalidOperationException>(() => editor.PasteClips(content, new(10), document.SubtitleTracks[targetIndex].Id));

        Assert.Same(document, editor.Snapshot);
        Assert.Equal(0, changes);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.Equal<ProjectLayer>([document.Layers[0], primary, document.Layers[4]], content.Layers);
    }

    [Fact]
    public void MissingTargetAndTargetCollisionPreserveSnapshotRedoAndPayload()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document, [document.Layers[0].Id, primary.Id], primary.Id);
        var editor = new ProjectEditor(document);
        editor.ShiftClips([document.Layers[0].Id], new(1));
        Assert.True(editor.Undo());
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.Throws<KeyNotFoundException>(() => editor.PasteClips(content, new(10), Guid.NewGuid()));
        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content, MediaTime.Zero, document.SubtitleTracks[2].Id));

        Assert.Same(document, editor.Snapshot);
        Assert.Equal(0, changes);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Same(primary, content.Layers[1]);
        Assert.Same(document.Subtitles[1], content.Subtitles[0]);
        Assert.Equal(document.SubtitleTracks[1].Id, content.ReferenceTrackId);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("missingSubtitleTrack")]
    [InlineData("missingReferenceTrack")]
    [InlineData("emptyIdentity")]
    public void InvalidFrozenTrackMetadataCannotPublishAPaste(string corruption)
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var content = ProjectEditingOperations.CaptureClips(document, [primary.Id], primary.Id, document.SubtitleTracks[2].Id);
        var ids = content.SourceTrackIds;
        content = content with
        {
            SourceTrackIds = corruption switch
            {
                "default" => default,
                "empty" => [],
                "duplicate" => ids.Add(ids[0]),
                "missingSubtitleTrack" => ids.RemoveAt(1),
                "missingReferenceTrack" => ids.RemoveAt(2),
                "emptyIdentity" => ids.SetItem(4, Guid.Empty),
                _ => throw new ArgumentOutOfRangeException(nameof(corruption))
            }
        };
        var editor = new ProjectEditor(document);

        Assert.Throws<InvalidDataException>(() => editor.PasteClips(content, new(10), document.SubtitleTracks[3].Id));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Equal(document.SubtitleTracks[1].Id, content.Subtitles[0].TrackId);
    }

    [Fact]
    public void LegacyClipboardWithoutFrozenMetadataRetainsOriginalTrackPasteCompatibility()
    {
        var document = CreateDocument();
        var primary = document.Layers[2];
        var captured = ProjectEditingOperations.CaptureClips(document, [primary.Id], primary.Id);
        var legacy = new ClipClipboardContent(captured.SourceProjectId, captured.PrimaryId,
            captured.EarliestStart, captured.Layers, captured.Subtitles);

        var result = ProjectEditingOperations.PasteClips(document, legacy, new(10));

        Assert.Equal(document.SubtitleTracks[1].Id, result.Document.Subtitles[^1].TrackId);
        Assert.Throws<InvalidDataException>(() =>
            ProjectEditingOperations.PasteClips(document, legacy, new(10), document.SubtitleTracks[2].Id));
        var implicitReference = captured with { ReferenceTrackId = null };
        var mapped = ProjectEditingOperations.PasteClips(document, implicitReference, new(10), document.SubtitleTracks[2].Id);
        Assert.Equal(document.SubtitleTracks[2].Id, mapped.Document.Subtitles[^1].TrackId);
    }

    [Fact]
    public void GraphicOnlyPasteDoesNotAttachShapesOrImagesToTheTargetSubtitleTrack()
    {
        var document = CreateDocument();
        var shape = document.Layers[0];
        var image = document.Layers[6];
        var content = ProjectEditingOperations.CaptureClips(document, [shape.Id, image.Id], shape.Id,
            document.SubtitleTracks[3].Id);

        var result = ProjectEditingOperations.PasteClips(document, content, new(10), document.SubtitleTracks[0].Id);

        Assert.Equal<SubtitleLine>(document.Subtitles, result.Document.Subtitles);
        Assert.All(result.Document.Layers.Skip(document.Layers.Length), layer => Assert.Null(layer.SubtitleId));
        Assert.Equal(shape.Shape, result.Document.Layers[^2].Shape);
        Assert.Equal(image.Image, result.Document.Layers[^1].Image);
        Assert.Equal(document.Assets, result.Document.Assets);
    }

    private static ProjectDocument CreateDocument()
    {
        var tracks = Enumerable.Range(0, 5).Select(index => new SubtitleTrack
        {
            Name = $"Track {index}", DefaultStyle = new() { FontSize = 90 + index },
            StylePresetId = Guid.NewGuid(), StylePresetName = $"Preset {index}"
        }).ToImmutableArray();
        var lines = tracks.Select((track, index) => new SubtitleLine
        {
            TrackId = track.Id, Start = new(index % 2, 3), End = new(6 + index % 2, 3),
            Text = $"cue {index}", Style = new() { FontSize = 30 + index }
        }).ToImmutableArray();
        var layers = lines.Select(line => new ProjectLayer
        {
            Id = line.Id, SubtitleId = line.Id, Kind = LayerKind.SUBTITLE, Start = line.Start, End = line.End
        }).ToImmutableArray();
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 30, 40) };
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png");
        var image = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(imageAsset.Id, 50, 60) };
        return new()
        {
            Assets = [imageAsset], SubtitleTracks = tracks, Subtitles = lines,
            Layers = layers.Insert(0, shape).Add(image)
        };
    }
}
