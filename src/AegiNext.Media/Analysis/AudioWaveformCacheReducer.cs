namespace AegiNext.Media.Analysis;

internal sealed class AudioWaveformCacheReducer
{
    internal const int BASE_SAMPLES = 512;
    private readonly AudioAnalysisCacheStore store;
    private readonly AudioWaveformCacheLevel[] levels;
    private int samplesInBucket;
    private float minimum;
    private float maximum;

    internal AudioWaveformCacheReducer(AudioAnalysisCacheStore store)
    {
        this.store = store;
        levels = Enumerable.Range(9, 22).Select(exponent => new AudioWaveformCacheLevel(1 << exponent)).ToArray();
    }

    internal void Append(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var count = Math.Min(BASE_SAMPLES - samplesInBucket, samples.Length);
            var (low, high) = AudioWaveformPeakReducer.Reduce(samples[..count]);
            minimum = Math.Min(minimum, low);
            maximum = Math.Max(maximum, high);
            samplesInBucket += count;
            samples = samples[count..];
            if (samplesInBucket == BASE_SAMPLES)
            {
                Emit(0, minimum, maximum);
                samplesInBucket = 0;
                minimum = 0;
                maximum = 0;
            }
        }
    }

    internal void FlushPages()
    {
        foreach (var level in levels)
        {
            Flush(level);
        }
    }

    internal void Complete()
    {
        if (samplesInBucket > 0)
        {
            Emit(0, minimum, maximum);
            samplesInBucket = 0;
        }
        for (var index = 0; index < levels.Length - 1; index++)
        {
            var level = levels[index];
            if (level.HasPending)
            {
                level.HasPending = false;
                Emit(index + 1, level.Minimum, level.Maximum);
            }
        }
        FlushPages();
    }

    private void Emit(int index, float low, float high)
    {
        var level = levels[index];
        if (level.Count == 0)
        {
            level.FirstBucket = level.NextBucket;
        }
        level.Peaks[level.Count * 2] = low;
        level.Peaks[level.Count * 2 + 1] = high;
        level.Count++;
        level.NextBucket++;
        if (level.Count == level.Peaks.Length / 2)
        {
            Flush(level);
        }
        if (index == levels.Length - 1)
        {
            return;
        }
        if (!level.HasPending)
        {
            level.Minimum = low;
            level.Maximum = high;
            level.HasPending = true;
        }
        else
        {
            level.HasPending = false;
            Emit(index + 1, Math.Min(level.Minimum, low), Math.Max(level.Maximum, high));
        }
    }

    private void Flush(AudioWaveformCacheLevel level)
    {
        if (level.Count == 0)
        {
            return;
        }
        store.AppendWaveform(level.SamplesPerBucket, level.FirstBucket, level.Peaks.AsSpan(0, level.Count * 2));
        level.Count = 0;
    }
}
