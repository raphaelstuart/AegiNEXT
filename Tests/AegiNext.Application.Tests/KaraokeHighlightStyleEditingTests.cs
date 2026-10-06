using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class KaraokeHighlightStyleEditingTests
{
    [Fact]
    public void SnapshotRoundTripUndoAndLegacyV3RemainIndependentOfPersonalPresetLibrary()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab");
        var original = editor.Snapshot;
        var style = Highlight();
        editor.UpdateSubtitle(id, line => line with { KaraokeStyle = style, Karaoke = [new(0, 2, new(0), new(2), SceneColor.White)] });
        var applied = editor.Snapshot;
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(applied));
        Assert.Equal(style, Assert.Single(restored.Subtitles).KaraokeStyle);
        Assert.Equal(Assert.Single(applied.Subtitles).Karaoke.ToArray(), Assert.Single(restored.Subtitles).Karaoke.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);

        var legacy = JsonNode.Parse(ProjectStore.Serialize(applied))!;
        legacy["subtitles"]![0]!.AsObject().Remove("karaokeStyle");
        restored = ProjectStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString()));
        Assert.Null(Assert.Single(restored.Subtitles).KaraokeStyle);
        Assert.Equal(2, Assert.Single(restored.Subtitles).Karaoke.Length);
        Assert.Equal(applied.Subtitles[0].Karaoke.ToArray(), restored.Subtitles[0].Karaoke.ToArray());
    }

    [Fact]
    public void SplitInheritsSnapshotOnlyOnPiecesThatContainHighlightSegments()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab");
        var style = Highlight();
        editor.UpdateSubtitle(id, line => line with { KaraokeStyle = style, Karaoke = [new(0, 1, new(0), new(2), SceneColor.White)] });
        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, id, new(2), 1);
        Assert.Equal(style, split.Subtitles[0].KaraokeStyle);
        Assert.Null(split.Subtitles[1].KaraokeStyle);
        Assert.Empty(split.Subtitles[1].Karaoke);
        var merged = ProjectEditingOperations.MergeSubtitles(split, split.Subtitles[0].Id, split.Subtitles[1].Id, "");
        Assert.Equal(style, Assert.Single(merged.Subtitles).KaraokeStyle);
    }

    [Fact]
    public void MergePreservesDifferentVisualsAndAcceptsEquivalentPresetSources()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "a");
        var second = editor.AddSubtitle(new(2), new(4), "b");
        var style = Highlight();
        editor.UpdateSubtitle(first, line => line with { KaraokeStyle = style, Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] });
        editor.UpdateSubtitle(second, line => line with { KaraokeStyle = style with { Fill = SceneColor.Black }, Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] });
        var mixed = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second);
        Assert.Null(Assert.Single(mixed.Subtitles).KaraokeStyle);
        Assert.Equal(style.Fill, mixed.Subtitles[0].Karaoke[0].ActiveStyle!.Fill);
        Assert.Equal(SceneColor.Black, mixed.Subtitles[0].Karaoke[1].ActiveStyle!.Fill);
        editor.UpdateSubtitle(second, line => line with { KaraokeStyle = style with { PresetId = Guid.NewGuid(), PresetName = "Copy" } });
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second);
        Assert.Equal(style, Assert.Single(merged.Subtitles).KaraokeStyle);
        Assert.Equal(2, Assert.Single(merged.Subtitles).Karaoke.Length);
    }

    [Fact]
    public void MergeUsesSecondSnapshotWhenOnlySecondHasHighlight()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "a");
        var second = editor.AddSubtitle(new(2), new(4), "b");
        var style = Highlight();
        editor.UpdateSubtitle(second, line => line with { KaraokeStyle = style, Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] });
        var merged = ProjectEditingOperations.MergeSubtitles(editor.Snapshot, first, second);
        Assert.Equal(style, Assert.Single(merged.Subtitles).KaraokeStyle);
        Assert.Equal(new MediaTime(2), Assert.Single(Assert.Single(merged.Subtitles).Karaoke).Start);
    }

    [Fact]
    public void CropAndStretchPreserveSnapshotAndRetainExactHighlightClock()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "ab");
        var style = Highlight();
        editor.UpdateSubtitle(id, line => line with { KaraokeStyle = style, Karaoke = [new(0, 2, new(0), new(4), SceneColor.White)] });
        var ids = editor.Snapshot.Subtitles[0].Karaoke.Select(clip => clip.Id).ToArray();
        editor.SetSubtitleTiming(id, new(1), new(3), TimelineEditMode.CROP);
        Assert.Equal(style, Assert.Single(editor.Snapshot.Subtitles).KaraokeStyle);
        Assert.Equal(new MediaTime(2), editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.Equal(new MediaTime(4), editor.Snapshot.Subtitles[0].Karaoke[^1].End);
        editor.SetSubtitleTiming(id, new(1), new(5), TimelineEditMode.STRETCH);
        Assert.Equal(style, Assert.Single(editor.Snapshot.Subtitles).KaraokeStyle);
        Assert.Equal(new MediaTime(4), editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.Equal(new MediaTime(8), editor.Snapshot.Subtitles[0].Karaoke[^1].End);
        Assert.Equal(ids, editor.Snapshot.Subtitles[0].Karaoke.Select(clip => clip.Id));
        ProjectValidator.Validate(ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot)));
    }

    private static KaraokeHighlightStyle Highlight()
    {
        return KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "HDR highlight", new()
        {
            Fill = new(4, -0.2, 3, 0.5), Stroke = new(0, 1, 0), StrokeWidth = 3,
            ShadowOffset = new(6, -4), ShadowColor = new(0, 0, 2, 0.6), ShadowBlur = 5
        });
    }
}
