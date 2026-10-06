using System.Collections.Immutable;
using AegiNext.Application.Timing;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class TimingPostProcessorTests
{
    [Fact]
    public void DefaultsMatchTheOriginalProcessorAndInvalidSettingsAreRejected()
    {
        var options = new TimingPostProcessorOptions();
        Assert.Equal(100, options.LeadInMilliseconds);
        Assert.Equal(350, options.LeadOutMilliseconds);
        Assert.Equal(90, options.BiasPercent);
        Assert.True(options.KeyframeSnapEnabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { MaximumGapMilliseconds = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { EndAfterMilliseconds = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { BiasPercent = 101 }).Validate());
    }

    [Fact]
    public void LeadProcessingAndAdjacencyUseExactTimesAndOneUndoTransaction()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(11, 5), new(3));
        var document = Document(first, second);
        var editor = new ProjectEditor(document);
        var notifications = 0;
        editor.Changed += (_, _) => notifications++;
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300, BiasPercent = 90 };

        editor.Apply("Timing post-processor", value => TimingPostProcessor.Process(value, options, Styles()));

        var after = editor.Snapshot;
        Assert.Equal(new MediaTime(109, 50), after.Subtitles[0].End);
        Assert.Equal(after.Subtitles[0].End, after.Subtitles[1].Start);
        Assert.Equal(1, notifications);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
    }

    [Fact]
    public void StylesAndSelectionIntersectAndExcludedSubtitlesRetainTheirIdentity()
    {
        var first = Line(new(1), new(2), "Dialogue");
        var second = Line(new(4), new(5), "Signs");
        var third = Line(new(7), new(8), "Dialogue");
        var document = Document(first, second, third);
        var options = Disabled() with { LeadOutEnabled = true, LeadOutMilliseconds = 120 };

        var after = TimingPostProcessor.Process(document, options, new HashSet<string> { "Dialogue" },
            new HashSet<Guid> { first.Id, second.Id });

        Assert.Equal(new MediaTime(53, 25), after.Subtitles[0].End);
        Assert.Same(second, after.Subtitles[1]);
        Assert.Same(third, after.Subtitles[2]);
        Assert.Same(document.Layers[1], after.Layers[1]);
    }

    [Fact]
    public void AdjacencyDoesNotConnectParallelTracksOrExcludedSubtitles()
    {
        var otherTrack = Guid.NewGuid();
        var first = Line(new(1), new(2));
        var second = Line(new(11, 5), new(3)) with { TrackId = otherTrack };
        var document = Document(first, second) with
        {
            SubtitleTracks = [SubtitleTrack.Default, new() { Id = otherTrack, Name = "Other" }]
        };
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300 };
        Assert.Same(document, TimingPostProcessor.Process(document, options, Styles()));

        var sameTrack = Document(first, second with { TrackId = first.TrackId, StyleName = "Signs" });
        Assert.Same(sameTrack, TimingPostProcessor.Process(sameTrack, options, Styles()));
    }

    [Fact]
    public void LeadProcessingRestrainsNeighboursBeforeTheWholeBatchIsValidated()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(21, 10), new(3));
        var document = Document(first, second);
        var options = Disabled() with
        {
            LeadInEnabled = true, LeadInMilliseconds = 300,
            LeadOutEnabled = true, LeadOutMilliseconds = 350
        };

        var result = TimingPostProcessor.Process(document, options, Styles());

        Assert.Equal(new MediaTime(7, 10), result.Subtitles[0].Start);
        Assert.Equal(new MediaTime(2), result.Subtitles[1].Start);
        Assert.Equal(result.Subtitles[1].Start, result.Subtitles[0].End);
        Assert.Equal(new MediaTime(67, 20), result.Subtitles[1].End);
    }

    [Fact]
    public void ExcludedNeighbourCollisionRejectsTheBatchAndPreservesRedoAndNotifications()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(21, 10), new(3), "Signs");
        var document = Document(first, second);
        var editor = new ProjectEditor(document);
        editor.Apply("Temporary", value => value with { Name = "temporary" });
        editor.Undo();
        var notifications = 0;
        editor.Changed += (_, _) => notifications++;
        var options = Disabled() with { LeadOutEnabled = true, LeadOutMilliseconds = 350 };

        Assert.Throws<TimingPostProcessorException>(() => editor.Apply("Timing post-processor",
            value => TimingPostProcessor.Process(value, options, Styles())));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void CropKeepsAbsoluteContentOriginAndBothKaraokeClocksInNestedLayers()
    {
        var line = Line(new(2), new(4)) with
        {
            Text = "AB", Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)]
        };
        var originalLayer = Layer(line) with
        {
            AnimationOffset = new(1, 4),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0.25), new(new(3, 2), 0.75)])]
        };
        var group = new ProjectLayer { Kind = LayerKind.GROUP, Start = new(0), End = new(10), Children = [originalLayer] };
        var document = Document(line) with { Layers = [group] };
        var options = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 100 };

        var after = TimingPostProcessor.Process(document, options, Styles());

        var changed = after.Layers[0].Children[0];
        Assert.Equal(originalLayer.Start - originalLayer.AnimationOffset, changed.Start - changed.AnimationOffset);
        Assert.Equal(new MediaTime(3, 20), changed.AnimationOffset);
        Assert.Equal(originalLayer.Tracks, changed.Tracks);
        Assert.Equal(line.Karaoke, after.Subtitles[0].Karaoke);
        Assert.Equal(line.InactiveKaraoke, after.Subtitles[0].InactiveKaraoke);
        Assert.Equal(group.Start, after.Layers[0].Start);
        Assert.Equal(group.End, after.Layers[0].End);
    }

    [Fact]
    public void DisabledEmptyAndUnmatchedProcessingPreservesTheSnapshot()
    {
        var document = Document(Line(new(1), new(2)));
        Assert.Same(document, TimingPostProcessor.Process(document, Disabled(), Styles()));
        Assert.Same(document, TimingPostProcessor.Process(document, new(), new HashSet<string>()));
        Assert.Same(document, TimingPostProcessor.Process(document, new(), Styles(), new HashSet<Guid>()));
        Assert.Same(document, TimingPostProcessor.Process(document, Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 0 }, Styles()));
    }

    [Fact]
    public void KeyframeSnappingRequiresAnIndexWhenThereAreTargets()
    {
        var document = Document(Line(new(1), new(2)));
        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document,
            Disabled() with { KeyframeSnapEnabled = true }, Styles()));
    }

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(50, 2100)]
    [InlineData(90, 2180)]
    [InlineData(100, 2200)]
    public void AdjacencyBiasIncludesBothEndpointsAndTheMaximumGap(int bias, int expectedMilliseconds)
    {
        var document = Document(Line(new(1), new(2)), Line(new(11, 5), new(3)));
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 200, BiasPercent = bias };
        var after = TimingPostProcessor.Process(document, options, Styles());
        Assert.Equal(new MediaTime(expectedMilliseconds, 1000), after.Subtitles[0].End);
        Assert.Equal(after.Subtitles[0].End, after.Subtitles[1].Start);
    }

    [Theory]
    [InlineData(85, 180)]
    [InlineData(115, 225)]
    public void KeyframeWindowsAreInclusive(int startCentiseconds, int endCentiseconds)
    {
        var document = Document(Line(new(startCentiseconds, 100), new(endCentiseconds, 100)));
        var index = new VideoTimingIndex([new(0), new(1, 10), new(4, 5), new(1), new(11, 10), new(6, 5), new(2)], [0, 3, 6]);
        var options = Disabled() with
        {
            KeyframeSnapEnabled = true, StartBeforeMilliseconds = 150, StartAfterMilliseconds = 150,
            EndBeforeMilliseconds = 200, EndAfterMilliseconds = 250
        };

        var after = TimingPostProcessor.Process(document, options, Styles(), video: index);

        Assert.Equal(new MediaTime(1), after.Subtitles[0].Start);
        Assert.Equal(new MediaTime(2), after.Subtitles[0].End);
    }

    [Fact]
    public void KeyframeWindowsDoNotSnapTimesBeyondTheConfiguredLimits()
    {
        var document = Document(Line(new(21, 25), new(113, 50)));
        var index = new VideoTimingIndex([new(0), new(1, 10), new(4, 5), new(1), new(11, 10), new(6, 5), new(2)], [0, 3, 6]);
        var options = Disabled() with { KeyframeSnapEnabled = true, StartBeforeMilliseconds = 150 };
        Assert.Same(document, TimingPostProcessor.Process(document, options, Styles(), video: index));
    }

    [Fact]
    public void LeadInIsClampedToTheMediaOriginWithoutQuantizingTheOtherBoundary()
    {
        var document = Document(Line(new(1, 20), new(1, 3)));
        var options = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 100 };
        var result = TimingPostProcessor.Process(document, options, Styles());
        Assert.Equal(MediaTime.Zero, result.Subtitles[0].Start);
        Assert.Equal(new MediaTime(1, 3), result.Subtitles[0].End);
    }

    [Fact]
    public void MissingKeyframeCandidatesAreReportedAsAProcessingFailure()
    {
        var document = Document(Line(new(1), new(2)));
        var index = new VideoTimingIndex([new(0), new(1), new(2)], []);
        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document,
            Disabled() with { KeyframeSnapEnabled = true }, Styles(), video: index));
    }

    [Fact]
    public void EndAtAnExactBoundaryUsesThePrecedingFrameAndCollapseRejectsTheWholeBatch()
    {
        var document = Document(Line(new(17, 20), new(6, 5)));
        var index = new VideoTimingIndex([new(0), new(1, 10), new(4, 5), new(1), new(11, 10), new(6, 5), new(2)], [0, 3, 6]);
        var options = Disabled() with { KeyframeSnapEnabled = true };
        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document, options, Styles(), video: index));
    }

    private static TimingPostProcessorOptions Disabled()
    {
        return new() { LeadInEnabled = false, LeadOutEnabled = false, AdjacencyEnabled = false, KeyframeSnapEnabled = false };
    }

    private static HashSet<string> Styles()
    {
        return new(StringComparer.Ordinal) { "Default" };
    }

    private static SubtitleLine Line(MediaTime start, MediaTime end, string styleName = "Default")
    {
        return new() { Start = start, End = end, Text = "line", StyleName = styleName };
    }

    private static ProjectDocument Document(params SubtitleLine[] lines)
    {
        return new() { Subtitles = lines.ToImmutableArray(), Layers = lines.Select(Layer).ToImmutableArray() };
    }

    private static ProjectLayer Layer(SubtitleLine line)
    {
        return new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
    }
}
