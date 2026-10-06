using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTrackStyleTests
{
    [Fact]
    public void TrackStyleAppliesExistingSubtitlesOnceAndFutureCreationAndImportInherit()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(2), "one");
        var otherTrack = editor.AddSubtitleTrack("Other");
        var other = editor.AddSubtitle(new(0), new(2), "other", otherTrack);
        editor.SetKeyframe(first, AnimationProperty.OPACITY, new(new(1), 0.4));
        var before = editor.Snapshot;
        var presetId = Guid.NewGuid();
        var style = new SubtitleStyle { FontSize = 42, Bold = true, Fill = new(2, 0.1, 0.3) };
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, presetId, "Dialogue", style, updateExisting: true);

        Assert.Equal(1, changes);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.Equal(style, editor.Snapshot.Subtitles.Single(line => line.Id == first).Style);
        Assert.Same(before.Subtitles.Single(line => line.Id == other), editor.Snapshot.Subtitles.Single(line => line.Id == other));
        Assert.Equal(style, editor.Snapshot.SubtitleTracks[0].DefaultStyle);
        Assert.Equal(presetId, editor.Snapshot.SubtitleTracks[0].StylePresetId);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());

        var added = editor.AddSubtitle(new(2), new(3), "new");
        editor.AddSubtitles([new SubtitleLine { Start = new(3), End = new(4), Text = "import", Style = new() { FontSize = 99 } }], SubtitleTrack.DEFAULT_TRACK_ID);
        Assert.Equal(style, editor.Snapshot.Subtitles.Single(line => line.Id == added).Style);
        Assert.Equal(style, editor.Snapshot.Subtitles.Single(line => line.Text == "import").Style);
        var stable = editor.Snapshot;
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, presetId, "Dialogue", style, updateExisting: true);
        Assert.Same(stable, editor.Snapshot);
    }

    [Fact]
    public void DefaultsOnlyPreservesExistingClipsAndOtherTracksInOneTransaction()
    {
        var editor = new ProjectEditor();
        var empty = editor.AddSubtitleTrack("Empty");
        var style = new SubtitleStyle { FontSize = 78, Italic = true };
        var presetId = Guid.NewGuid();
        editor.SetSubtitleTrackStyle(empty, presetId, "Empty default", style);
        Assert.Empty(editor.Snapshot.Subtitles);
        Assert.Equal(style, editor.Snapshot.SubtitleTracks.Single(track => track.Id == empty).DefaultStyle);
        editor.AddSubtitle(new(0), new(2), "one");
        editor.AddSubtitle(new(0), new(2), "two", empty);
        var before = editor.Snapshot;
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        var nextStyle = style with { FontSize = 95 };
        editor.SetSubtitleTrackStyle(empty, presetId, "New default", nextStyle);

        Assert.Equal(1, changes);
        Assert.Equal(nextStyle, editor.Snapshot.SubtitleTracks.Single(track => track.Id == empty).DefaultStyle);
        Assert.Same(before.SubtitleTracks[0], editor.Snapshot.SubtitleTracks[0]);
        Assert.Equal(before.Subtitles, editor.Snapshot.Subtitles);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void SplitAndCrossTrackMovePreserveEditedClipStyleAndEffects()
    {
        var editor = new ProjectEditor();
        var destination = editor.AddSubtitleTrack("Destination");
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Default", new() { FontSize = 40 });
        editor.SetSubtitleTrackStyle(destination, Guid.NewGuid(), "Destination default", new() { FontSize = 90 });
        var cue = editor.AddSubtitle(new(0), new(4), "abcd");
        var editedStyle = new SubtitleStyle { FontSize = 55, Italic = true };
        editor.UpdateSubtitle(cue, line => line with { Style = editedStyle });
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.4));
        editor.Apply("Split", document => ProjectEditingOperations.SplitSubtitle(document, cue, new(2), 2));
        Assert.All(editor.Snapshot.Subtitles, line => Assert.Equal(editedStyle, line.Style));
        var second = editor.Snapshot.Subtitles.Single(line => line.Id != cue);
        var layer = editor.Snapshot.Layers.Single(value => value.SubtitleId == second.Id);
        editor.MoveSubtitleToTrack(second.Id, destination);
        Assert.Equal(editedStyle, editor.Snapshot.Subtitles.Single(line => line.Id == second.Id).Style);
        Assert.Same(layer, editor.Snapshot.Layers.Single(value => value.SubtitleId == second.Id));
    }

    [Fact]
    public void VersionThreeRoundTripsTrackStylesAndMissingOptionalFieldsRemainSupported()
    {
        var editor = new ProjectEditor();
        editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "字幕样式", new() { FontSize = 41 });
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        var root = JsonNode.Parse(bytes)!.AsObject();
        var track = root["subtitleTracks"]![0]!.AsObject();
        track.Remove("defaultStyle");
        track.Remove("stylePresetId");
        track.Remove("stylePresetName");
        track.Remove("autoApplyStyle");
        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
        Assert.Null(Assert.Single(restored.SubtitleTracks).DefaultStyle);
        Assert.True(restored.SubtitleTracks[0].AutoApplyStyle);
        track["unknownStyle"] = true;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        var json = Encoding.UTF8.GetString(bytes);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(
            json.Replace("\"stylePresetName\":", "\"stylePresetName\": \"Duplicate\", \"stylePresetName\":", StringComparison.Ordinal))));
    }

    [Fact]
    public void InvalidTrackStyleAndFontReferencesAreRejectedAtomically()
    {
        var editor = new ProjectEditor();
        var before = editor.Snapshot;
        Assert.Throws<InvalidDataException>(() => editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, Guid.Empty, "Preset", new()));
        Assert.Throws<InvalidDataException>(() => editor.SetSubtitleTrackStyle(SubtitleTrack.DEFAULT_TRACK_ID, Guid.NewGuid(), "Preset", new() { FontAssetId = Guid.NewGuid() }));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void DisablingAutomaticStyleKeepsSnapshotAndCreationUsesProvidedFallbackOrBase()
    {
        var editor = new ProjectEditor();
        var trackId = SubtitleTrack.DEFAULT_TRACK_ID;
        var defaultStyle = new SubtitleStyle { FontSize = 44 };
        var fallback = new SubtitleStyle { FontSize = 91 };
        editor.SetSubtitleTrackStyle(trackId, Guid.NewGuid(), "Saved default", defaultStyle);
        var existing = editor.AddSubtitle(new(0), new(2), "existing");
        var before = editor.Snapshot;
        editor.SetSubtitleTrackAutoApplyStyle(trackId, false);
        Assert.Equal(before.Subtitles, editor.Snapshot.Subtitles);
        Assert.Equal(defaultStyle, editor.Snapshot.SubtitleTracks[0].DefaultStyle);
        Assert.False(editor.Snapshot.SubtitleTracks[0].AutoApplyStyle);
        var stored = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.False(stored.SubtitleTracks[0].AutoApplyStyle);
        var created = editor.AddSubtitle(new(2), new(4), "fallback", trackId, fallback);
        var baseline = editor.AddSubtitle(new(4), new(6), "base", trackId);
        editor.AddSubtitles([new SubtitleLine { Start = new(6), End = new(8), Text = "import" }], trackId, fallback);
        Assert.Equal(fallback, editor.Snapshot.Subtitles.Single(line => line.Id == created).Style);
        Assert.Equal(new SubtitleStyle(), editor.Snapshot.Subtitles.Single(line => line.Id == baseline).Style);
        Assert.Equal(fallback, editor.Snapshot.Subtitles.Single(line => line.Text == "import").Style);
        Assert.Equal(defaultStyle, editor.Snapshot.Subtitles.Single(line => line.Id == existing).Style);
        editor.SetSubtitleTrackAutoApplyStyle(trackId, true);
        var inherited = editor.AddSubtitle(new(8), new(10), "restored", trackId, fallback);
        Assert.Equal(defaultStyle, editor.Snapshot.Subtitles.Single(line => line.Id == inherited).Style);
    }
}
