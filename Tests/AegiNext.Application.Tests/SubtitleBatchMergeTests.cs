using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleBatchMergeTests
{
    private static readonly int[] highlightOffsets = [0, 6, 13];
    private static readonly string[] subtitleTexts = ["First", "Second", "Third"];

    [Fact]
    public void ThreeSelectedSubtitlesMergeInTimeOrderWithFirstIdentityStyleAndRebasedHighlights()
    {
        var document = CreateDocument();
        var first = document.Subtitles[0];
        var merged = ProjectEditingOperations.MergeSubtitles(document, document.Subtitles.Reverse().Select(line => line.Id));

        var cue = Assert.Single(merged.Subtitles);
        Assert.Equal(first.Id, cue.Id);
        Assert.Equal(first.Style, cue.Style);
        Assert.Equal("First\nSecond\nThird", cue.Text);
        Assert.Equal(MediaTime.Zero, cue.Start);
        Assert.Equal(new MediaTime(8), cue.End);
        var originalIds = document.Subtitles.Select(line => line.Karaoke[0].Id).ToHashSet();
        var firstClips = cue.Karaoke.Where(segment => originalIds.Contains(segment.Id)).ToArray();
        Assert.Equal(highlightOffsets, firstClips.Select(segment => segment.Utf16Start));
        Assert.Equal(new[] { MediaTime.Zero, new MediaTime(3), new MediaTime(6) }, firstClips.Select(segment => segment.Start));
        Assert.Equal(3, cue.Karaoke.Length);
        Assert.Equal([5, 6, 5], cue.Karaoke.Select(segment => segment.Utf16Length));
        Assert.Equal(first.Id, Assert.Single(merged.Layers).SubtitleId);
        Assert.Equal(3, document.Subtitles.Length);
        Assert.Equal(3, document.Layers.Length);
    }

    [Fact]
    public void FailureOnTheThirdSubtitleLeavesEditorSnapshotAndHistoryUnchanged()
    {
        var original = CreateDocument();
        var document = original with { Layers = original.Layers.SetItem(2, original.Layers[2] with { Opacity = 0.5 }) };
        var editor = new ProjectEditor(document);

        Assert.Throws<InvalidOperationException>(() => editor.Apply("Merge subtitles", value =>
            ProjectEditingOperations.MergeSubtitles(value, document.Subtitles.Select(line => line.Id))));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void BatchMergeRejectsMissingDuplicateAndNonadjacentTargets()
    {
        var document = CreateDocument();
        var first = document.Subtitles[0].Id;
        var last = document.Subtitles[2].Id;

        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.MergeSubtitles(document, new[] { first }));
        Assert.Throws<ArgumentException>(() => ProjectEditingOperations.MergeSubtitles(document, new[] { first, first }));
        Assert.Throws<KeyNotFoundException>(() => ProjectEditingOperations.MergeSubtitles(document, new[] { first, Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.MergeSubtitles(document, new[] { first, last }));
    }

    private static ProjectDocument CreateDocument()
    {
        var lines = subtitleTexts.Select((text, index) => new SubtitleLine
        {
            Text = text,
            Start = new(index * 3),
            End = new(index * 3 + 2),
            Style = new() { FontSize = 30 + index },
            Karaoke = [new(0, text.Length, MediaTime.Zero, new(2), SceneColor.White)]
        }).ToImmutableArray();
        return new ProjectDocument
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
    }
}
