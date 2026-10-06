using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

/// <summary>独立的局部波形缓存，每个均匀采样桶保存最小值和最大值。</summary>
public sealed class WaveformData
{
    /// <summary>复制并验证交替排列的最小值、最大值，缓存范围采用工程相对时间。</summary>
    public WaveformData(WaveformAnalysisRequest request, float[] peaks)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(peaks);
        if (peaks.Length != request.BucketCount * 2)
        {
            throw new ArgumentException("波形峰值长度与采样桶数不一致。", nameof(peaks));
        }

        var storage = peaks.ToArray();
        for (var index = 0; index < storage.Length; index += 2)
        {
            if (!float.IsFinite(storage[index]) || !float.IsFinite(storage[index + 1]) || storage[index] > storage[index + 1])
            {
                throw new ArgumentException("波形峰值必须有限且最小值不大于最大值。", nameof(peaks));
            }
        }

        Request = request;
        Peaks = storage;
    }

    public WaveformAnalysisRequest Request { get; }

    public MediaTime Start => Request.Start;

    public MediaTime Duration => Request.Duration;

    public MediaTime End => Request.End;

    public int SamplesPerBucket => Request.SamplesPerBucket;

    public int BucketCount => Request.BucketCount;

    public int PcmSampleRate { get; } = WaveformAnalyzer.SAMPLE_RATE;

    public ReadOnlyMemory<float> Peaks { get; }
}
