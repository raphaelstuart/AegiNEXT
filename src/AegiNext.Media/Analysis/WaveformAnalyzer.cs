using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

/// <summary>以固定 PCM 格式流式分析局部峰值，显示分辨率由采样桶大小决定。</summary>
public static class WaveformAnalyzer
{
    public const int SAMPLE_RATE = 48000;
    private static readonly MediaTimeBase sampleTimeBase = new(1, SAMPLE_RATE);

    /// <summary>在后台打开独立解码器并定位到指定波形范围，不影响播放或语谱分析。</summary>
    public static Task<WaveformData> AnalyzeFileAsync(string path, int streamIndex, MediaTime origin,
        WaveformAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() =>
        {
            using var source = FfmpegAudioDecoder.Open(path, streamIndex, new(SAMPLE_RATE, 1), cancellationToken);
            source.Seek(origin + request.Start, cancellationToken);
            return Analyze(source, origin, request, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>按块时间戳裁切半开范围并保留峰值与时间间隙，不定位或接管传入源。</summary>
    public static WaveformData Analyze(IAudioSampleSource source, MediaTime origin, WaveformAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        return Analyze(source, origin, request, static () => { }, cancellationToken);
    }

    internal static WaveformData Analyze(IAudioSampleSource source, MediaTime origin, WaveformAnalysisRequest request,
        Action checkRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (source.Format.SampleRate != SAMPLE_RATE || source.Format.Channels != 1)
        {
            throw new ArgumentException("波形分析要求 48 kHz 单声道 PCM。", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var peaks = new float[request.BucketCount * 2];
        var sampleCount = (long)request.SamplesPerBucket * request.BucketCount;
        var start = origin + request.Start;
        var end = origin + request.End;
        MediaTime? expected = null;
        while (source.Read(cancellationToken) is { } block)
        {
            checkRequest();
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Format != source.Format)
            {
                throw new InvalidDataException("PCM 块格式与波形分析源声明不一致。");
            }

            if (expected is { } expectedStart && block.Start < expectedStart)
            {
                throw new InvalidDataException("PCM 块时间戳发生重叠或回退。");
            }

            if (block.Start >= end)
            {
                break;
            }

            var blockEnd = block.Start + new MediaTime(block.FrameCount, SAMPLE_RATE);
            expected = blockEnd;
            var samples = block.Samples.Span;
            var blockOffset = (block.Start - start).ToTimestamp(sampleTimeBase, MediaTimeRounding.FLOOR).Value;
            if (blockOffset <= -samples.Length)
            {
                continue;
            }

            var first = (int)Math.Max(0, -blockOffset);
            var last = (int)Math.Min(samples.Length, sampleCount - blockOffset);
            for (var index = first; index < last; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    checkRequest();
                }

                var sample = samples[index];
                if (!float.IsFinite(sample))
                {
                    throw new InvalidDataException("PCM 包含非有限样本。");
                }

                var bucket = (int)((blockOffset + index) / request.SamplesPerBucket);
                peaks[bucket * 2] = Math.Min(peaks[bucket * 2], sample);
                peaks[bucket * 2 + 1] = Math.Max(peaks[bucket * 2 + 1], sample);
            }

            if (blockEnd >= end)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        checkRequest();
        var result = new WaveformData(request, peaks);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
