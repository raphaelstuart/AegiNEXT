using System.Collections.Immutable;
using AegiNext.Application.Timing;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class TimingPostProcessorBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedConfigurationsApplyAllLeadInsBeforeAnyLeadOutRegardlessOfMappingOrder(bool reverse)
    {
        var first = Line(new(1), new(2));
        var second = Line(new(21, 10), new(3));
        var document = Document(second, first);
        var leadOut = Disabled() with { LeadOutEnabled = true, LeadOutMilliseconds = 350 };
        var leadIn = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 300 };
        var configurations = new Dictionary<Guid, TimingPostProcessorOptions>();
        configurations.Add(reverse ? second.Id : first.Id, reverse ? leadIn : leadOut);
        configurations.Add(reverse ? first.Id : second.Id, reverse ? leadOut : leadIn);

        var after = TimingPostProcessor.Process(document, configurations);

        Assert.Equal(new MediaTime(2), after.Subtitles[0].Start);
        Assert.Equal(new MediaTime(2), after.Subtitles[1].End);
        Assert.Equal(second.End, after.Subtitles[0].End);
        Assert.Equal(first.Start, after.Subtitles[1].Start);
    }

    [Fact]
    public void UnselectedNeighboursClampBothLeadsAndKeepTheirRowsAndLayers()
    {
        var before = Line(new(1), new(2));
        var target = Line(new(21, 10), new(3));
        var after = Line(new(31, 10), new(4));
        var document = Document(before, target, after);
        var options = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 350, LeadOutEnabled = true, LeadOutMilliseconds = 350 };

        var result = TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions> { [target.Id] = options });

        Assert.Equal(before.End, result.Subtitles[1].Start);
        Assert.Equal(after.Start, result.Subtitles[1].End);
        Assert.Same(before, result.Subtitles[0]);
        Assert.Same(after, result.Subtitles[2]);
        Assert.Same(document.Layers[0], result.Layers[0]);
        Assert.Same(document.Layers[2], result.Layers[2]);
    }

    [Fact]
    public void AdjacencyCannotJumpAcrossAnUnselectedSubtitle()
    {
        var first = Line(new(1), new(2));
        var fixedLine = Line(new(41, 20), new(52, 25));
        var last = Line(new(21, 10), new(3));
        var document = Document(first, fixedLine, last);
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300 };

        var result = TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = options, [last.Id] = options });

        Assert.Same(document, result);
    }

    [Fact]
    public void AdjacencyRequiresEqualityOfTheEntireConfiguration()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(11, 5), new(3));
        var document = Document(first, second);
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300 };

        var result = TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions>
        {
            [first.Id] = options,
            [second.Id] = options with { LeadInMilliseconds = options.LeadInMilliseconds + 1 }
        });

        Assert.Same(document, result);
    }

    [Fact]
    public void ValueEquivalentConfigurationsConnectAdjacentSubtitlesWithDifferentStyleNames()
    {
        var first = Line(new(1), new(2)) with { StyleName = "Dialogue" };
        var second = Line(new(11, 5), new(3)) with { StyleName = "Signs" };
        var document = Document(second, first);
        var options = Disabled() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 200, BiasPercent = 90 };

        var result = TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = options, [second.Id] = options with { } });

        Assert.Equal(new MediaTime(109, 50), result.Subtitles[0].Start);
        Assert.Equal(result.Subtitles[0].Start, result.Subtitles[1].End);
        Assert.Equal(second.StyleName, result.Subtitles[0].StyleName);
        Assert.Equal(first.StyleName, result.Subtitles[1].StyleName);
    }

    [Fact]
    public void ParallelTracksDoNotConstrainLeadsOrConnectAdjacency()
    {
        var first = Line(new(1), new(2));
        var otherTrack = new SubtitleTrack { Name = "Other" };
        var second = Line(new(11, 5), new(3)) with { TrackId = otherTrack.Id };
        var document = Document(first, second) with { SubtitleTracks = [SubtitleTrack.Default, otherTrack] };
        var options = Disabled() with { LeadOutEnabled = true, LeadOutMilliseconds = 350, AdjacencyEnabled = true };

        var result = TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = options, [second.Id] = options });

        Assert.Equal(new MediaTime(47, 20), result.Subtitles[0].End);
        Assert.Equal(second.Start, result.Subtitles[1].Start);
        Assert.Equal(new MediaTime(67, 20), result.Subtitles[1].End);
    }

    [Fact]
    public void AdjacencyRunsAfterMixedLeadsHaveConsumedTheGap()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(12, 5), new(3));
        var document = Document(first, second);
        var options = Disabled() with
        {
            LeadInEnabled = true, LeadInMilliseconds = 100,
            LeadOutEnabled = true, LeadOutMilliseconds = 200,
            AdjacencyEnabled = true, MaximumGapMilliseconds = 200, BiasPercent = 50
        };

        var result = TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = options, [second.Id] = options });

        Assert.Equal(new MediaTime(9, 10), result.Subtitles[0].Start);
        Assert.Equal(new MediaTime(9, 4), result.Subtitles[0].End);
        Assert.Equal(result.Subtitles[0].End, result.Subtitles[1].Start);
        Assert.Equal(new MediaTime(16, 5), result.Subtitles[1].End);
    }

    [Fact]
    public void MixedKeyframeConfigurationsAreValidatedOnlyAfterTheFinalBatch()
    {
        var first = Line(new(1), new(2));
        var second = Line(new(21, 10), new(3));
        var document = Document(first, second);
        var firstOptions = Disabled() with { KeyframeSnapEnabled = true, StartBeforeMilliseconds = 0, StartAfterMilliseconds = 0, EndBeforeMilliseconds = 200, EndAfterMilliseconds = 0 };
        var secondOptions = firstOptions with { StartBeforeMilliseconds = 100, EndBeforeMilliseconds = 0 };
        var editor = new ProjectEditor(document);
        var notifications = 0;
        editor.Changed += (_, _) => notifications++;

        editor.Apply("Timing batch", value => TimingPostProcessor.Process(value,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = firstOptions, [second.Id] = secondOptions }, Index()));

        Assert.Equal(new MediaTime(11, 5), editor.Snapshot.Subtitles[0].End);
        Assert.Equal(editor.Snapshot.Subtitles[0].End, editor.Snapshot.Subtitles[1].Start);
        Assert.Equal(new MediaTime(3), editor.Snapshot.Subtitles[1].End);
        Assert.Equal(1, notifications);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InvalidKeyframeCollisionRejectsAllTracksAndPreservesRedoAndNotifications()
    {
        var first = Line(new(1), new(2));
        var fixedLine = Line(new(21, 10), new(3));
        var otherTrack = new SubtitleTrack { Name = "Other" };
        var otherTarget = Line(new(4), new(5)) with { TrackId = otherTrack.Id };
        var document = Document(first, fixedLine, otherTarget) with { SubtitleTracks = [SubtitleTrack.Default, otherTrack] };
        var editor = new ProjectEditor(document);
        editor.Apply("Temporary", value => value with { Name = "temporary" });
        editor.Undo();
        var notifications = 0;
        editor.Changed += (_, _) => notifications++;
        var firstOptions = Disabled() with { KeyframeSnapEnabled = true, StartBeforeMilliseconds = 0, StartAfterMilliseconds = 0, EndBeforeMilliseconds = 200, EndAfterMilliseconds = 0 };
        var otherOptions = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 100 };

        Assert.Throws<TimingPostProcessorException>(() => editor.Apply("Timing batch", value => TimingPostProcessor.Process(value,
            new Dictionary<Guid, TimingPostProcessorOptions> { [first.Id] = firstOptions, [otherTarget.Id] = otherOptions }, Index())));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void MixedConfigurationsPreserveCropContentOriginsKaraokeAndNestedGroups()
    {
        var first = Line(new(2), new(4)) with
        {
            Text = "AB", Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)],
            InactiveKaraoke = [new(1, 1, new(1), new(2), SceneColor.White)]
        };
        var second = Line(new(6), new(8));
        var firstLayer = Layer(first) with
        {
            AnimationOffset = new(1, 4),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0.25), new(new(3, 2), 0.75)])]
        };
        var secondLayer = Layer(second) with { AnimationOffset = new(1, 2) };
        var group = new ProjectLayer { Kind = LayerKind.GROUP, Start = new(0), End = new(10), Children = [firstLayer, secondLayer] };
        var document = Document(first, second) with { Layers = [group] };
        var options = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 100 };

        var after = TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions>
        {
            [first.Id] = options,
            [second.Id] = options with { LeadInMilliseconds = 250, LeadOutEnabled = true, LeadOutMilliseconds = 120 }
        });

        Assert.Equal(firstLayer.Start - firstLayer.AnimationOffset, after.Layers[0].Children[0].Start - after.Layers[0].Children[0].AnimationOffset);
        Assert.Equal(secondLayer.Start - secondLayer.AnimationOffset, after.Layers[0].Children[1].Start - after.Layers[0].Children[1].AnimationOffset);
        Assert.Equal(new MediaTime(3, 20), after.Layers[0].Children[0].AnimationOffset);
        Assert.Equal(new MediaTime(1, 4), after.Layers[0].Children[1].AnimationOffset);
        Assert.Equal(firstLayer.Tracks, after.Layers[0].Children[0].Tracks);
        Assert.Equal(first.Karaoke, after.Subtitles[0].Karaoke);
        Assert.Equal(first.InactiveKaraoke, after.Subtitles[0].InactiveKaraoke);
        Assert.Equal(group.Start, after.Layers[0].Start);
        Assert.Equal(group.End, after.Layers[0].End);
    }

    [Fact]
    public void EmptyDisabledAndZeroLeadBatchesKeepTheInputSnapshot()
    {
        var line = Line(new(1), new(2));
        var document = Document(line);

        Assert.Same(document, TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions>()));
        Assert.Same(document, TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = Disabled() }));
        Assert.Same(document, TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = Disabled() with { LeadInEnabled = true, LeadInMilliseconds = 0 } }));
    }

    [Fact]
    public void MissingTargetsInvalidOptionsAndUnavailableKeyframesRejectBeforeChanges()
    {
        var line = Line(new(1), new(2));
        var document = Document(line);

        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [Guid.NewGuid()] = Disabled() }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = Disabled() with { LeadInMilliseconds = -1 } }));
        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = Disabled() with { KeyframeSnapEnabled = true } }));
        Assert.Throws<TimingPostProcessorException>(() => TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = Disabled() with { KeyframeSnapEnabled = true } },
            new VideoTimingIndex([new(0), new(1), new(2)], [])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkippingUnavailableKeyframesPreservesConfigurationBoundariesAndOtherStages(bool emptyIndex)
    {
        var first = Line(new(1), new(2));
        var second = Line(new(11, 5), new(3));
        var document = Document(first, second);
        var options = Disabled() with
        {
            LeadInEnabled = true, LeadInMilliseconds = 100,
            AdjacencyEnabled = true, MaximumGapMilliseconds = 300,
            KeyframeSnapEnabled = true
        };
        var video = emptyIndex ? new VideoTimingIndex([new(0), new(1), new(2)], []) : null;

        var after = TimingPostProcessor.Process(document, new Dictionary<Guid, TimingPostProcessorOptions>
        {
            [first.Id] = options,
            [second.Id] = options with { KeyframeSnapEnabled = false }
        }, video, skipUnavailableKeyframes: true);

        Assert.Equal(new MediaTime(9, 10), after.Subtitles[0].Start);
        Assert.Equal(first.End, after.Subtitles[0].End);
        Assert.Equal(new MediaTime(21, 10), after.Subtitles[1].Start);
        Assert.Equal(second.End, after.Subtitles[1].End);
        Assert.True(options.KeyframeSnapEnabled);
    }

    [Fact]
    public void AllowingUnavailableKeyframesStillSnapsWhenVideoIsAvailable()
    {
        var line = Line(new(1), new(2));
        var document = Document(line);
        var options = Disabled() with { KeyframeSnapEnabled = true, EndBeforeMilliseconds = 200, EndAfterMilliseconds = 0 };

        var after = TimingPostProcessor.Process(document,
            new Dictionary<Guid, TimingPostProcessorOptions> { [line.Id] = options }, Index(), skipUnavailableKeyframes: true);

        Assert.Equal(new MediaTime(11, 5), after.Subtitles[0].End);
    }

    private static TimingPostProcessorOptions Disabled()
    {
        return new() { LeadInEnabled = false, LeadOutEnabled = false, AdjacencyEnabled = false, KeyframeSnapEnabled = false };
    }

    private static VideoTimingIndex Index()
    {
        return new([new(0), new(9, 10), new(1), new(3, 2), new(8, 5), new(17, 10), new(9, 5), new(19, 10), new(2), new(21, 10), new(11, 5), new(3)], [2, 10, 11]);
    }

    private static SubtitleLine Line(MediaTime start, MediaTime end)
    {
        return new() { Start = start, End = end, Text = "line" };
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
