using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class VideoFrameNavigatorCacheTests
{
    [Fact]
    public void CacheBudgetQueryFailureReleasesTransferredRawFramesExactlyOnce()
    {
        var failure = new InvalidDataException("Invalid source plane layout.");
        var decoder = new FakeVideoDecoder(0, 40)
        {
            FrameFactory = index => new(index * 40, index) { PlaneInfoFailure = failure }
        };
        using (var navigator = new VideoFrameNavigator(_ => decoder))
        {
            Assert.Same(failure, Assert.Throws<InvalidDataException>(() => navigator.ReadFrame()));
        }

        Assert.Equal(1, decoder.DisposeCount);
        Assert.Equal(2, decoder.IssuedFrames.Count);
        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(-64)]
    public void StridePaddingCountsTowardTheByteBudgetAndEvictsOldFrames(int stride)
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120, 160, 200, 240)
        {
            FrameFactory = index => new(index * 40, index) { SourceStride = stride }
        };
        using (var navigator = new VideoFrameNavigator(_ => decoder, maximumCachedFrames: 120, maximumCachedBytes: 128))
        {
            using var first = navigator.SeekFrame(new(200, 1000));
            using var hit = navigator.SeekFrame(new(160, 1000));
            Assert.Single(decoder.SeekTargets);
            using var evicted = navigator.SeekFrame(new(120, 1000));
            Assert.Equal(2, decoder.SeekTargets.Count);
        }

        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void FrameCountBudgetAppliesEvenWhenAllPixelsFitTheByteBudget()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120, 160, 200, 240);
        using var navigator = new VideoFrameNavigator(_ => decoder, maximumCachedFrames: 2);
        using var first = navigator.SeekFrame(new(200, 1000));
        using var hit = navigator.SeekFrame(new(160, 1000));
        Assert.Single(decoder.SeekTargets);
        using var evicted = navigator.SeekFrame(new(120, 1000));
        Assert.Equal(2, decoder.SeekTargets.Count);
    }

    [Fact]
    public void AFrameLargerThanTheByteBudgetIsDeliveredWithoutBeingCached()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120, 160)
        {
            FrameFactory = index => new(index * 40, index) { SourceStride = 128 }
        };
        using var navigator = new VideoFrameNavigator(_ => decoder, maximumCachedFrames: 120, maximumCachedBytes: 64);
        using var first = navigator.SeekFrame(new(120, 1000));
        using var second = navigator.SeekFrame(new(120, 1000));
        Assert.Equal(2, decoder.SeekTargets.Count);
    }

    [Fact]
    public void OutstandingLeaseSurvivesCacheEvictionAndNavigatorDisposal()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120);
        var navigator = new VideoFrameNavigator(_ => decoder, maximumCachedFrames: 1);
        using var first = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        var original = Assert.Single(decoder.IssuedFrames, frame => frame.Marker == 0);
        try
        {
            using (var next = navigator.ReadFrame())
            {
                Assert.Equal(0, original.DisposeCount);
                Assert.Equal(0, first.Frame.CopyPlane(0)[0]);
            }

            navigator.Dispose();
            Assert.Equal(0, original.DisposeCount);
            Assert.Equal(0, first.Frame.CopyPlane(0)[0]);
            var retained = first.Frame;
            first.Dispose();
            Assert.Equal(1, original.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => retained.CopyPlane(0));
            Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            navigator.Dispose();
        }
    }

    [Fact]
    public void ReverseAdjacentTargetsReusePrerollFramesWithoutAnotherNativeSeekOrRead()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120, 160, 200, 240, 280);
        using (var navigator = new VideoFrameNavigator(_ => decoder))
        {
            using var first = navigator.SeekFrame(new(200, 1000));
            var reads = decoder.ReadCount;
            foreach (var target in new[] { 160, 120, 80, 40 })
            {
                using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(target, 1000)));
                Assert.Equal(new MediaTime(target, 1000), selected.Time);
                Assert.Equal(target / 40, selected.Frame.CopyPlane(0)[0]);
            }

            Assert.Single(decoder.SeekTargets);
            Assert.Equal(reads, decoder.ReadCount);
        }

        Assert.All(decoder.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public void PlaybackAfterACachedBackwardSeekFollowsExactLogicalPtsUntilItRejoinsTheDecoder()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120, 160, 200, 240, 280, 320);
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var first = navigator.SeekFrame(new(200, 1000));
        var reads = decoder.ReadCount;
        using var reverse = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(80, 1000)));
        foreach (var expected in new[] { 120, 160, 200 })
        {
            using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
            Assert.Equal(new MediaTime(expected, 1000), next.Time);
        }

        Assert.Equal(reads, decoder.ReadCount);
        using var physical = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        Assert.Equal(new MediaTime(240, 1000), physical.Time);
        Assert.Equal(new MediaTime(280, 1000), physical.NextFrameTime);
        Assert.Single(decoder.SeekTargets);
        Assert.Equal(reads + 1, decoder.ReadCount);
    }

    [Fact]
    public void CachedSameFrameAndEofRequestsRemainExactWithoutRepeatingDecode()
    {
        var decoder = new FakeVideoDecoder(0, 40, 80, 120);
        using var navigator = new VideoFrameNavigator(_ => decoder);
        using var first = navigator.SeekFrame(new(10));
        var reads = decoder.ReadCount;
        using var tail = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(20)));
        Assert.True(tail.ReachedEnd);
        Assert.Equal(new MediaTime(120, 1000), tail.Time);
        Assert.Null(navigator.ReadFrame());
        using var reverse = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(new(50, 1000)));
        Assert.Equal(new MediaTime(40, 1000), reverse.Time);
        Assert.Equal(new MediaTime(80, 1000), reverse.NextFrameTime);
        Assert.Equal(reads, decoder.ReadCount);
        using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        Assert.Equal(new MediaTime(80, 1000), next.Time);
    }
}
