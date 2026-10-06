using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Analysis;

public sealed class WaveformAnalyzerTests
{
    [Fact]
    public void ChunkingDoesNotChangePeaksOrLoseImpulsesAtBlockAndBucketBoundaries()
    {
        var samples = new float[64];
        samples[7] = 0.75F;
        samples[8] = -0.9F;
        samples[16] = 0.95F;
        samples[63] = -0.8F;
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 8, 8);
        using var single = Source([Block(MediaTime.Zero, samples)]);
        using var chunked = Source(Chunk(samples, 7));

        var first = WaveformAnalyzer.Analyze(single, MediaTime.Zero, request);
        var second = WaveformAnalyzer.Analyze(chunked, MediaTime.Zero, request);

        Assert.Equal(first.Peaks.ToArray(), second.Peaks.ToArray());
        Assert.Equal(0.75F, first.Peaks.Span[1]);
        Assert.Equal(-0.9F, first.Peaks.Span[2]);
        Assert.Equal(0.95F, first.Peaks.Span[5]);
        Assert.Equal(-0.8F, first.Peaks.Span[14]);
        Assert.Equal(0, single.DisposeCount);
    }

    [Fact]
    public void FinerBucketsRecoverLocalDetailAndCoarserBucketsPreserveExtrema()
    {
        var samples = new float[32];
        samples[2] = 0.8F;
        samples[13] = -0.9F;
        using var coarseSource = Source([Block(MediaTime.Zero, samples)]);
        using var fineSource = Source([Block(MediaTime.Zero, samples)]);

        var coarse = WaveformAnalyzer.Analyze(coarseSource, MediaTime.Zero, new(MediaTime.Zero, 16, 2));
        var fine = WaveformAnalyzer.Analyze(fineSource, MediaTime.Zero, new(MediaTime.Zero, 4, 8));

        Assert.Equal(coarse.Duration, fine.Duration);
        Assert.Equal(-0.9F, coarse.Peaks.Span[0]);
        Assert.Equal(0.8F, coarse.Peaks.Span[1]);
        Assert.Equal(0.8F, fine.Peaks.Span[1]);
        Assert.Equal(0, fine.Peaks.Span[2]);
        Assert.Equal(0, fine.Peaks.Span[3]);
        Assert.Equal(-0.9F, fine.Peaks.Span[6]);
        Assert.Equal(coarse.Peaks.Span[0], fine.Peaks.ToArray().Min());
        Assert.Equal(coarse.Peaks.Span[1], fine.Peaks.ToArray().Max());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-4)]
    public void NonzeroOriginAndRangeCropExactHalfOpenSampleBoundaries(int originSeconds)
    {
        var origin = new MediaTime(originSeconds);
        var start = new MediaTime(8, WaveformAnalyzer.SAMPLE_RATE);
        var samples = new float[18];
        samples[7] = 0.99F;
        samples[8] = -0.5F;
        samples[15] = 0.75F;
        samples[16] = -0.99F;
        using var source = Source([Block(origin, samples)]);
        var request = new WaveformAnalysisRequest(start, 4, 2);

        var data = WaveformAnalyzer.Analyze(source, origin, request);

        Assert.Equal(start, data.Start);
        Assert.Equal(new MediaTime(8, WaveformAnalyzer.SAMPLE_RATE), data.Duration);
        Assert.Equal(new[] { -0.5F, 0F, 0F, 0.75F }, data.Peaks.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FractionalOriginMapsSamplesWithoutFloatingPointBoundaryDrift(int halfSampleOrigin)
    {
        var origin = new MediaTime(halfSampleOrigin, WaveformAnalyzer.SAMPLE_RATE * 2);
        using var source = Source([Block(MediaTime.Zero, [0.99F, 0.4F, -0.5F, 0.6F, -0.99F])]);

        var data = WaveformAnalyzer.Analyze(source, origin, new(MediaTime.Zero, 1, 3));

        var expected = halfSampleOrigin > 0
            ? new[] { 0F, 0.4F, -0.5F, 0F, 0F, 0.6F }
            : new[] { 0F, 0.99F, 0F, 0.4F, -0.5F, 0F };
        Assert.Equal(expected, data.Peaks.ToArray());
    }

    [Fact]
    public void BlockStartingAtRangeEndDoesNotRepeatItsSamplesIntoTheLastBucket()
    {
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 4, 2);
        using var source = Source([Block(request.End, [0.9F])]);

        var data = WaveformAnalyzer.Analyze(source, MediaTime.Zero, request);

        Assert.All(data.Peaks.ToArray(), peak => Assert.Equal(0, peak));
    }

    [Fact]
    public void CompletedRangeDoesNotReadOrFailOnTheFollowingBlock()
    {
        var readCount = 0;
        using var source = new AnalysisAudioSource([Block(MediaTime.Zero, [0.5F, -0.5F])])
        {
            Format = new(WaveformAnalyzer.SAMPLE_RATE, 1),
            BeforeRead = () =>
            {
                readCount++;
                if (readCount > 1)
                {
                    throw new InvalidDataException("范围结束后的读取不应发生。");
                }
            }
        };

        var data = WaveformAnalyzer.Analyze(source, MediaTime.Zero, new(MediaTime.Zero, 1, 2));

        Assert.Equal(1, readCount);
        Assert.Equal(0.5F, data.Peaks.Span[1]);
        Assert.Equal(-0.5F, data.Peaks.Span[2]);
    }

    [Fact]
    public void TimestampGapsAndEarlyEofRemainSilent()
    {
        var origin = new MediaTime(4);
        using var source = Source([
            Block(origin, [-0.5F, 0.5F]),
            Block(origin + new MediaTime(8, WaveformAnalyzer.SAMPLE_RATE), [-0.8F, 0.8F])]);

        var data = WaveformAnalyzer.Analyze(source, origin, new(MediaTime.Zero, 2, 8));

        Assert.Equal(new[] { -0.5F, 0.5F, 0F, 0F, 0F, 0F, 0F, 0F, -0.8F, 0.8F, 0F, 0F, 0F, 0F, 0F, 0F },
            data.Peaks.ToArray());
    }

    [Fact]
    public void LongMediaLocalRangeHasDetailAndStorageIsBoundedByBucketCount()
    {
        var start = new MediaTime(3 * 60 * 60);
        var samples = new float[128];
        samples[64] = 0.8F;
        using var source = Source([Block(start, samples)]);
        var data = WaveformAnalyzer.Analyze(source, MediaTime.Zero, new(start, 64, 2));

        Assert.Equal(start, data.Start);
        var expected = new[] { 0F, 0F, 0F, 0.8F };
        Assert.Equal(expected, data.Peaks.ToArray());
        using var empty = Source([]);
        var bounded = WaveformAnalyzer.Analyze(empty, MediaTime.Zero,
            new(MediaTime.Zero, 65536, WaveformAnalysisRequest.MAX_BUCKET_COUNT));
        Assert.Equal(WaveformAnalysisRequest.MAX_BUCKET_COUNT * 2, bounded.Peaks.Length);
        Assert.All(bounded.Peaks.ToArray(), peak => Assert.Equal(0, peak));
    }

    [Theory]
    [InlineData(34)]
    [InlineData(35)]
    public void RequestAlignsToStableProjectGridAndDataOwnsItsStorage(int halfSamples)
    {
        var request = new WaveformAnalysisRequest(new(halfSamples, WaveformAnalyzer.SAMPLE_RATE * 2), 8, 2);
        Assert.Equal(new MediaTime(16, WaveformAnalyzer.SAMPLE_RATE), request.Start);
        Assert.Equal(new MediaTime(16, WaveformAnalyzer.SAMPLE_RATE), request.Duration);
        Assert.Equal(new MediaTime(32, WaveformAnalyzer.SAMPLE_RATE), request.End);
        var peaks = new[] { -0.5F, 0.5F, 0F, 0.8F };
        var data = new WaveformData(request, peaks);
        peaks[1] = 0;
        Assert.Equal(0.5F, data.Peaks.Span[1]);
        Assert.Equal(request.Start, data.Start);
        Assert.Equal(request.Duration, data.Duration);
        Assert.Equal(8, data.SamplesPerBucket);
        Assert.Equal(2, data.BucketCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(-1, 1)]
    [InlineData(8, 0)]
    [InlineData(8, 16385)]
    public void InvalidRequestResolutionOrStorageSizeIsRejected(int samplesPerBucket, int bucketCount)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WaveformAnalysisRequest(MediaTime.Zero, samplesPerBucket, bucketCount));
    }

    [Fact]
    public void InvalidDataFormatsAndBackwardTimestampsAreRejected()
    {
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 1, 4);
        Assert.ThrowsAny<ArgumentException>(() => new WaveformAnalysisRequest(new(-1), 8, 1));
        Assert.ThrowsAny<ArgumentException>(() => new WaveformData(request, [0F, 0F]));
        Assert.ThrowsAny<ArgumentException>(() => new WaveformData(request, [0F, float.NaN, 0F, 0F, 0F, 0F, 0F, 0F]));
        Assert.ThrowsAny<ArgumentException>(() => new WaveformData(request, [0.5F, -0.5F, 0F, 0F, 0F, 0F, 0F, 0F]));
        using var wrongSource = new AnalysisAudioSource([]);
        Assert.Throws<ArgumentException>(() => WaveformAnalyzer.Analyze(wrongSource, MediaTime.Zero, request));
        using var wrongBlock = Source([new(new(16000, 1), MediaTime.Zero, [0F])]);
        Assert.Throws<InvalidDataException>(() => WaveformAnalyzer.Analyze(wrongBlock, MediaTime.Zero, request));
        using var nonFinite = Source([Block(MediaTime.Zero, [float.PositiveInfinity])]);
        Assert.Throws<InvalidDataException>(() => WaveformAnalyzer.Analyze(nonFinite, MediaTime.Zero, request));
        using var backwards = Source([
            Block(MediaTime.Zero, [0F, 0F]),
            Block(new(1, WaveformAnalyzer.SAMPLE_RATE), [0F])]);
        Assert.Throws<InvalidDataException>(() => WaveformAnalyzer.Analyze(backwards, MediaTime.Zero, request));
    }

    [Fact]
    public void CancellationDoesNotPublishPartialDataOrTakeSourceOwnership()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new AnalysisAudioSource([Block(MediaTime.Zero, [0.5F])])
        {
            Format = new(WaveformAnalyzer.SAMPLE_RATE, 1),
            BeforeRead = cancellation.Cancel
        };
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 1, 1);
        Assert.ThrowsAny<OperationCanceledException>(() => WaveformAnalyzer.Analyze(source, MediaTime.Zero, request, cancellation.Token));
        Assert.Equal(0, source.DisposeCount);
        Assert.ThrowsAny<OperationCanceledException>(() => WaveformAnalyzer.Analyze(source, MediaTime.Zero, request, cancellation.Token));
    }

    private static AnalysisAudioSource Source(IEnumerable<AudioSampleBlock> blocks)
    {
        return new(blocks) { Format = new(WaveformAnalyzer.SAMPLE_RATE, 1) };
    }

    private static AudioSampleBlock Block(MediaTime start, float[] samples)
    {
        return new(new(WaveformAnalyzer.SAMPLE_RATE, 1), start, samples);
    }

    private static IEnumerable<AudioSampleBlock> Chunk(float[] samples, int size)
    {
        for (var offset = 0; offset < samples.Length; offset += size)
        {
            yield return Block(new(offset, WaveformAnalyzer.SAMPLE_RATE),
                samples.AsSpan(offset, Math.Min(size, samples.Length - offset)).ToArray());
        }
    }
}
