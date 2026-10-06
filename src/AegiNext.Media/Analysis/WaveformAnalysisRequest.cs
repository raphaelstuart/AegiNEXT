using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

/// <summary>在固定工程样本网格上定义有界波形范围和峰值时间分辨率。</summary>
public sealed record WaveformAnalysisRequest
{
    public const int MAX_BUCKET_COUNT = 16384;

    /// <summary>将工程相对起点向下对齐到采样桶，桶内样本数必须为二的幂。</summary>
    public WaveformAnalysisRequest(MediaTime start, int samplesPerBucket, int bucketCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerBucket);
        if ((samplesPerBucket & (samplesPerBucket - 1)) != 0)
        {
            throw new ArgumentException("波形采样桶的样本数必须为二的幂。", nameof(samplesPerBucket));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bucketCount, MAX_BUCKET_COUNT);
        var sample = start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
        Start = new(sample - sample % samplesPerBucket, WaveformAnalyzer.SAMPLE_RATE);
        SamplesPerBucket = samplesPerBucket;
        BucketCount = bucketCount;
        Duration = new((long)samplesPerBucket * bucketCount, WaveformAnalyzer.SAMPLE_RATE);
        End = Start + Duration;
    }

    public MediaTime Start { get; }

    public MediaTime Duration { get; }

    public MediaTime End { get; }

    public int SamplesPerBucket { get; }

    public int BucketCount { get; }
}
