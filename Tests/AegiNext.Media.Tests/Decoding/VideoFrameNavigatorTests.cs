using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class VideoFrameNavigatorTests
{
    [Theory]
    [InlineData(-100, -40, 0, true, false)]
    [InlineData(-40, -40, 0, false, false)]
    [InlineData(-1, -40, 0, false, false)]
    [InlineData(0, 0, 30, false, false)]
    [InlineData(29, 0, 30, false, false)]
    [InlineData(30, 30, 100, false, false)]
    [InlineData(99, 30, 100, false, false)]
    [InlineData(100, 100, null, false, true)]
    [InlineData(10000, 100, null, false, true)]
    public void SelectsTheContainingHalfOpenIntervalAndExplicitBoundaryCases(int target, int expected, int? next, bool beforeFirst, bool reachedEnd)
    {
        var decoders = new List<FakeVideoDecoder>();
        using (var navigator = new VideoFrameNavigator(_ =>
        {
            var decoder = new FakeVideoDecoder(-40, 0, 30, 100);
            decoders.Add(decoder);
            return decoder;
        }))
        using (var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(target, 1000))))
        {
            Assert.Equal(new MediaTime(expected, 1000), selected.Time);
            Assert.Equal(next is { } value ? new MediaTime(value, 1000) : null, selected.NextFrameTime);
            Assert.Equal(beforeFirst, selected.IsBeforeFirst);
            Assert.Equal(reachedEnd, selected.ReachedEnd);
            Assert.All(decoders, decoder => Assert.Equal(0, decoder.CancelCount));
        }

        Assert.All(decoders, decoder => Assert.Equal(1, decoder.DisposeCount));
        Assert.All(decoders.SelectMany(decoder => decoder.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void KeepsLookaheadForTheNextReadAfterSeeking()
    {
        var decoder = new FakeVideoDecoder(0, 40, 120, 160);
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(50, 1000)));
        using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());

        Assert.Equal(new MediaTime(40, 1000), selected.Time);
        Assert.Equal(new MediaTime(120, 1000), next.Time);
        Assert.Equal(new MediaTime(160, 1000), next.NextFrameTime);
        Assert.Equal(1, selected.Frame.CopyPlane(0)[0]);
        Assert.Equal(2, next.Frame.CopyPlane(0)[0]);
    }

    [Fact]
    public void DuplicateTimestampsUseTheLastFrameInTheGroup()
    {
        var decoder = new FakeVideoDecoder(0, 40, 40, 100);
        using (var navigator = new VideoFrameNavigator(_ => decoder))
        using (var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(40, 1000))))
        {
            Assert.Equal(2, selected.Frame.CopyPlane(0)[0]);
            Assert.Equal(new MediaTime(100, 1000), selected.NextFrameTime);
            Assert.All(decoder.IssuedFrames.Where(frame => frame.Marker == 1), frame => Assert.Equal(1, frame.DisposeCount));
        }

        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void ComparesExactTimesAcrossDifferentTimeBasesWithoutMillisecondRounding()
    {
        var decoder = new FakeVideoDecoder(1, 2, 3)
        {
            FrameFactory = index => index switch
            {
                0 => new(1, 0, new(1, 3)),
                1 => new(2, 1, new(1, 6)),
                _ => new(3, 2, new(1, 3))
            }
        };
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(1, 3)));

        Assert.Equal(new MediaTime(1, 3), selected.Time);
        Assert.Equal(new MediaTime(1), selected.NextFrameTime);
        Assert.Equal(1, selected.Frame.CopyPlane(0)[0]);
    }

    [Fact]
    public void ASeekLandingAfterTheTargetReopensFromTheProvenBeginning()
    {
        var first = new FakeVideoDecoder(-40, 0, 30, 100) { SeekLandingIndex = 3 };
        var fresh = new FakeVideoDecoder(-40, 0, 30, 100);
        var created = 0;
        using (var navigator = new VideoFrameNavigator(_ => created++ == 0 ? first : fresh))
        using (var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(10, 1000))))
        {
            Assert.Equal(2, created);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(new MediaTime(0), selected.Time);
            Assert.Equal(new MediaTime(30, 1000), selected.NextFrameTime);
            Assert.False(selected.IsBeforeFirst);
            Assert.Empty(fresh.SeekTargets);
        }

        Assert.All(first.IssuedFrames.Concat(fresh.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void ASeekLandingAtEofStillReturnsTheLastFrameOfANonemptySource()
    {
        var first = new FakeVideoDecoder(0, 40, 100) { SeekLandingIndex = 3 };
        var fresh = new FakeVideoDecoder(0, 40, 100);
        var created = 0;
        using (var navigator = new VideoFrameNavigator(_ => created++ == 0 ? first : fresh))
        using (var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(10))))
        {
            Assert.Equal(2, created);
            Assert.Equal(new MediaTime(100, 1000), selected.Time);
            Assert.Null(selected.NextFrameTime);
            Assert.True(selected.ReachedEnd);
            Assert.Equal(2, selected.Frame.CopyPlane(0)[0]);
        }

        Assert.All(first.IssuedFrames.Concat(fresh.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void MissingRawPtsUsesBestEffortForNavigationAndKeepsTheOriginalFactMissing()
    {
        var decoder = new FakeVideoDecoder(0, null)
        {
            FrameFactory = index => new(index == 0 ? 0 : null, index, bestEffortTimestamp: 40)
        };
        using (var navigator = new VideoFrameNavigator(_ => decoder))
        {
            using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(40, 1000)));
            Assert.Equal(new MediaTime(40, 1000), selected.Time);
            Assert.Null(selected.Frame.Info.PresentationTimestamp);
            Assert.Equal(new MediaTimestamp(40, new(1, 1000)), selected.Frame.Info.BestEffortTimestamp);
            Assert.Equal(VideoDisplayTimingEvidence.BestEffortTimestamp, selected.Frame.Info.DisplayTiming!.Evidence);
            Assert.False(selected.Frame.Info.DisplayTiming.IsDerived);
            Assert.True(selected.ReachedEnd);
        }

        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void RegressingPtsFailsInsteadOfSortingOrGuessingFrameRate()
    {
        var decoder = new FakeVideoDecoder(0, 40, 20, 100);
        using (var navigator = new VideoFrameNavigator(_ => decoder))
        {
            Assert.Throws<InvalidDataException>(() => navigator.SeekFrame(new(50, 1000)));
        }

        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void EmptySourceHasNoSelectedFrameAndRepeatedEofIsStable()
    {
        var decoder = new FakeVideoDecoder();
        using var navigator = new VideoFrameNavigator(_ => decoder);

        Assert.Null(navigator.ReadFrame());
        Assert.Null(navigator.ReadFrame());
        Assert.Null(navigator.SeekFrame(MediaTime.Zero));
    }

    [Fact]
    public void PreCancelledRequestDoesNotCancelOrAdvanceTheSharedDecoder()
    {
        var decoder = new FakeVideoDecoder(0, 40);
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.ThrowsAny<OperationCanceledException>(() => navigator.SeekFrame(MediaTime.Zero, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, decoder.ReadCount);
        Assert.Empty(decoder.SeekTargets);
        Assert.Equal(0, decoder.CancelCount);
        using var first = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        Assert.Equal(MediaTime.Zero, first.Time);
    }

    [Fact]
    [SuppressMessage("ReSharper", "DisposeOnUsingVariable", Justification = "Repeated disposal and ownership transfer are the subject of this test.")]
    public void PublishedFrameSurvivesNavigatorDisposalAndOnlyItsOwnerReleasesIt()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80);
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        var frame = Assert.Single(decoder.IssuedFrames, item => item.Marker == 0);

        navigator.Dispose();
        navigator.Dispose();

        Assert.Equal(0, frame.DisposeCount);
        Assert.Equal(1, decoder.DisposeCount);
        Assert.Equal(0, Assert.Single(selected.Frame.CopyPlane(0)));
        selected.Dispose();
        selected.Dispose();
        Assert.Equal(1, frame.DisposeCount);
        Assert.All(decoder.IssuedFrames, item => Assert.Equal(1, item.DisposeCount));
    }
}
