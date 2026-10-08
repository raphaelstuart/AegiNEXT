using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class VideoDisplayTimingTests
{
    [Theory]
    [InlineData(VideoDisplayTimingEvidence.OriginalPts, false)]
    [InlineData(VideoDisplayTimingEvidence.BestEffortTimestamp, false)]
    [InlineData(VideoDisplayTimingEvidence.BestEffortTimestamp | VideoDisplayTimingEvidence.PreviousFrameDuration, true)]
    [InlineData(VideoDisplayTimingEvidence.OriginalPts | VideoDisplayTimingEvidence.DeclaredFrameRate, true)]
    [InlineData(VideoDisplayTimingEvidence.StreamStart, true)]
    public void SeparateDisplayTimeRetainsItsEvidenceAndNeverCreatesRawTimestamps(VideoDisplayTimingEvidence evidence, bool derived)
    {
        var rawPts = evidence == VideoDisplayTimingEvidence.OriginalPts ? 125L : (long?)null;
        var bestEffort = evidence == VideoDisplayTimingEvidence.BestEffortTimestamp ? 125L : (long?)null;
        using var frame = new FakeVideoFrame(rawPts, 0, bestEffortTimestamp: bestEffort, displayTiming: Timing(125, evidence));

        Assert.Equal(rawPts, frame.Info.PresentationTimestampValue);
        Assert.Equal(bestEffort, frame.Info.BestEffortTimestampValue);
        if (derived)
        {
            Assert.Null(frame.Info.PresentationTimestamp);
            Assert.Null(frame.Info.BestEffortTimestamp);
        }
        Assert.Equal(new MediaTimestamp(125, new(1, 1000)), frame.Info.DisplayTiming!.Timestamp);
        Assert.Equal(evidence, frame.Info.DisplayTiming.Evidence);
        Assert.Equal(derived, frame.Info.DisplayTiming.IsDerived);
    }

    [Theory]
    [InlineData(4u, 1L, 1, 1000)]
    [InlineData(32u, 1L, 1, 1000)]
    [InlineData(3u, 1L, 1, 1000)]
    [InlineData(1u, long.MinValue, 1, 1000)]
    [InlineData(1u, 1L, 0, 1000)]
    [InlineData(1u, 1L, 1, 0)]
    public void InvalidNativeDisplayTimeDoesNotBecomeNavigable(uint evidence, long timestamp, int numerator, int denominator)
    {
        var timing = new NativeFrameDisplayTiming
        {
            evidence = evidence,
            timestamp = timestamp,
            timeBaseNum = numerator,
            timeBaseDen = denominator
        };

        Assert.Throws<InvalidDataException>(() => new FakeVideoFrame(null, 0, displayTiming: timing));
    }

    [Fact]
    public void NoEvidenceLeavesDisplayTimeUnavailableEvenWhenNumericFieldsContainValues()
    {
        using var frame = new FakeVideoFrame(null, 0, displayTiming: Timing(125, 0));

        Assert.Null(frame.Info.DisplayTiming);
    }

    [Theory]
    [InlineData(VideoDisplayTimingEvidence.OriginalPts)]
    [InlineData(VideoDisplayTimingEvidence.BestEffortTimestamp)]
    public void DirectDisplayTimingMustMatchItsOriginalFact(VideoDisplayTimingEvidence evidence)
    {
        Assert.Throws<InvalidDataException>(() => new FakeVideoFrame(null, 0, displayTiming: Timing(125, evidence)));
        Assert.Throws<InvalidDataException>(() => new FakeVideoFrame(120, 0, bestEffortTimestamp: 120, displayTiming: Timing(125, evidence)));
    }

    [Fact]
    public void NavigatorUsesDerivedTailAndResumesWithoutLosingTheFinalFrame()
    {
        var decoder = new FakeVideoDecoder(0, null, null)
        {
            FrameFactory = index => index switch
            {
                0 => new(0, 0),
                1 => new(null, 1, bestEffortTimestamp: 40),
                _ => new(null, 2, displayTiming: Timing(100,
                    VideoDisplayTimingEvidence.BestEffortTimestamp | VideoDisplayTimingEvidence.PreviousFrameDuration))
            }
        };
        using var navigator = new VideoFrameNavigator(_ => decoder, 0);
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(40, 1000)));
        using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());

        Assert.Equal(new MediaTime(40, 1000), selected.Time);
        Assert.Equal(new MediaTime(100, 1000), selected.NextFrameTime);
        Assert.Equal(new MediaTime(100, 1000), next.Time);
        Assert.Equal(2, Assert.Single(next.Frame.CopyPlane(0)));
        Assert.True(next.Frame.Info.DisplayTiming!.IsDerived);
        Assert.Null(next.Frame.Info.PresentationTimestamp);
        Assert.Null(next.Frame.Info.BestEffortTimestamp);
        Assert.True(next.ReachedEnd);
        Assert.Null(navigator.ReadFrame());
        Assert.Null(navigator.ReadFrame());
    }

    [Fact]
    public void BestEffortOrDerivedRegressionStillFailsInsteadOfSortingFrames()
    {
        var decoder = new FakeVideoDecoder(0, null, null)
        {
            FrameFactory = index => index switch
            {
                0 => new(0, 0),
                1 => new(null, 1, bestEffortTimestamp: 100),
                _ => new(null, 2, displayTiming: Timing(40,
                    VideoDisplayTimingEvidence.BestEffortTimestamp | VideoDisplayTimingEvidence.PreviousFrameDuration))
            }
        };
        using var navigator = new VideoFrameNavigator(_ => decoder, 0);

        Assert.Throws<InvalidDataException>(() => navigator.SeekFrame(new(100, 1000)));
    }

    [Fact]
    public void MissingAllTimingEvidenceStillFails()
    {
        var decoder = new FakeVideoDecoder(0, null);
        using var navigator = new VideoFrameNavigator(_ => decoder, 0);

        Assert.Throws<InvalidDataException>(() => navigator.ReadFrame());
    }

    [Fact]
    public void MissingSeekAnchorReopensOnceFromTheBeginningAndKeepsContinuation()
    {
        var initial = new FakeVideoDecoder(0, 40, 80);
        initial.FrameFactory = index => initial.SeekTargets.Count > 0
            ? throw new VideoDisplayTimingUnavailableException("No anchor after keyframe seeking.") : new(index * 40, index);
        var fresh = new FakeVideoDecoder(0, 40, 80);
        var created = 0;
        using (var navigator = new VideoFrameNavigator(_ => created++ == 0 ? initial : fresh, 0))
        {
            using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(40, 1000)));
            using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
            Assert.Equal(2, created);
            Assert.Equal(1, initial.DisposeCount);
            Assert.Equal(new MediaTime(40, 1000), selected.Time);
            Assert.Equal(new MediaTime(80, 1000), next.Time);
            Assert.Empty(fresh.SeekTargets);
        }
        Assert.All(initial.IssuedFrames.Concat(fresh.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void ACorruptDecodeErrorAfterSeekingNeverTriggersTimingRecovery()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80);
        decoder.FrameFactory = index => decoder.SeekTargets.Count > 0
            ? throw new InvalidDataException("Corrupt media.") : new(index * 40, index);
        var created = 0;
        using var navigator = new VideoFrameNavigator(_ =>
        {
            created++;
            return decoder;
        }, 0);

        Assert.Throws<InvalidDataException>(() => navigator.SeekFrame(new(40, 1000)));
        Assert.Equal(1, created);
    }

    [Fact]
    public void TimingRecoveryDoesNotRetryAnUnusableSourceFromTheBeginning()
    {
        var initial = new FakeVideoDecoder(0, 40, 80);
        initial.FrameFactory = index => initial.SeekTargets.Count > 0
            ? throw new VideoDisplayTimingUnavailableException("No anchor after keyframe seeking.") : new(index * 40, index);
        var unusable = new FakeVideoDecoder((long?)null);
        var created = 0;
        using var navigator = new VideoFrameNavigator(_ => created++ == 0 ? initial : unusable, 0);

        Assert.Throws<InvalidDataException>(() => navigator.SeekFrame(new(40, 1000)));
        Assert.Equal(2, created);
        Assert.Empty(unusable.SeekTargets);
    }

    [Fact]
    public void SupersededTimingRecoveryKeepsTheTerminalDecoderMarkedForRestart()
    {
        var superseded = false;
        var initial = new FakeVideoDecoder(0, 40, 80);
        initial.FrameFactory = index =>
        {
            if (initial.SeekTargets.Count > 0)
            {
                superseded = true;
                throw new VideoDisplayTimingUnavailableException("No anchor after keyframe seeking.");
            }
            return new(index * 40, index);
        };
        var fresh = new FakeVideoDecoder(0, 40, 80);
        var created = 0;
        using var navigator = new VideoFrameNavigator(_ => created++ == 0 ? initial : fresh, 0);

        Assert.Throws<VideoSeekSupersededException>(() => navigator.SeekFrame(new(40, 1000), () => superseded));
        Assert.Equal(1, created);
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(40, 1000)));
        Assert.Equal(2, created);
        Assert.Equal(new MediaTime(40, 1000), selected.Time);
    }

    private static NativeFrameDisplayTiming Timing(long value, VideoDisplayTimingEvidence evidence)
    {
        return new()
        {
            evidence = (uint)evidence,
            timestamp = value,
            timeBaseNum = 1,
            timeBaseDen = 1000
        };
    }
}
