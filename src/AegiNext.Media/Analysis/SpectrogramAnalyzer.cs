using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

/// <summary>
/// 流式计算对数频率语谱图，不将整段 PCM 存入内存；独立解码不影响播放会话。
/// </summary>
public static class SpectrogramAnalyzer
{
    public const int SAMPLE_RATE = 16000;
    public const int FFT_SIZE = 1024;
    public const int HOP_SIZE = 256;
    public const int FREQUENCY_BINS = 128;
    public const int MAX_COLUMNS = 4096;

    /// <summary>
    /// 在后台打开独立的单声道解码器并分析给定工程时间范围。
    /// </summary>
    public static Task<SpectrogramData> AnalyzeFileAsync(string path, int streamIndex, MediaTime origin,
        MediaTime duration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() =>
        {
            using var source = FfmpegAudioDecoder.Open(path, streamIndex, new(SAMPLE_RATE, 1), cancellationToken);
            return Analyze(source, origin, duration, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// 从 16 kHz mono PCM 源读取；不接管源所有权。时间间隙保持空白，取消不返回部分缓存。
    /// </summary>
    public static SpectrogramData Analyze(IAudioSampleSource source, MediaTime origin, MediaTime duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Format.SampleRate != SAMPLE_RATE || source.Format.Channels != 1 || duration <= MediaTime.Zero ||
            duration > new MediaTime(6 * 60 * 60))
        {
            throw new ArgumentException("语谱分析要求 16 kHz mono 和不超过六小时的明确时长。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var seconds = ToSeconds(duration);
        var width = Math.Clamp((int)Math.Ceiling(seconds * SAMPLE_RATE / HOP_SIZE), 1, MAX_COLUMNS);
        var levels = new byte[checked(width * FREQUENCY_BINS)];
        var waveform = new float[checked(width * 2)];
        var window = new float[FFT_SIZE];
        var power = new double[FFT_SIZE / 2 + 1];
        var transform = new SpectrumTransform(FFT_SIZE);
        var rows = GetFrequencyRows();
        var count = 0;
        var windowStart = 0.0;
        MediaTime? expected = null;
        var end = origin + duration;
        while (source.Read(cancellationToken) is { } block)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Format != source.Format)
            {
                throw new InvalidDataException("PCM 块格式与分析源声明不一致。");
            }

            if (expected is { } expectedStart && block.Start < expectedStart)
            {
                throw new InvalidDataException("PCM 块时间戳发生重叠或回退。");
            }

            if (block.Start >= end)
            {
                break;
            }

            if (expected is { } previous && Math.Abs(ToSeconds(block.Start - previous)) > 2.0 / SAMPLE_RATE)
            {
                count = 0;
            }

            var samples = block.Samples.Span;
            var blockStart = ToSeconds(block.Start - origin);
            for (var index = 0; index < samples.Length; index++)
            {
                var time = blockStart + index / (double)SAMPLE_RATE;
                if (time < 0 || time >= seconds)
                {
                    continue;
                }

                var sample = samples[index];
                if (!float.IsFinite(sample))
                {
                    throw new InvalidDataException("PCM 包含非有限样本。");
                }

                var column = GetColumn(time, seconds, width);
                waveform[column * 2] = Math.Min(waveform[column * 2], sample);
                waveform[column * 2 + 1] = Math.Max(waveform[column * 2 + 1], sample);
                if (count == 0)
                {
                    windowStart = time;
                }

                window[count++] = sample;
                if (count != FFT_SIZE)
                {
                    continue;
                }

                transform.Power(window, power);
                column = GetColumn(windowStart + FFT_SIZE / 2.0 / SAMPLE_RATE, seconds, width);
                for (var bin = 1; bin < power.Length; bin++)
                {
                    var decibels = 10 * Math.Log10(Math.Max(power[bin], 1e-12));
                    var intensity = (byte)Math.Clamp((decibels + 80) * 255 / 80, 0, 255);
                    var offset = rows[bin] * width + column;
                    levels[offset] = Math.Max(levels[offset], intensity);
                }

                Array.Copy(window, HOP_SIZE, window, 0, FFT_SIZE - HOP_SIZE);
                count = FFT_SIZE - HOP_SIZE;
                windowStart += HOP_SIZE / (double)SAMPLE_RATE;
            }

            expected = block.Start + new MediaTime(block.FrameCount, SAMPLE_RATE);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(width, FREQUENCY_BINS, duration, levels, waveform);
    }

    private static int[] GetFrequencyRows()
    {
        var result = new int[FFT_SIZE / 2 + 1];
        var span = Math.Log(SAMPLE_RATE / 2.0 / 40);
        for (var bin = 1; bin < result.Length; bin++)
        {
            var frequency = bin * SAMPLE_RATE / (double)FFT_SIZE;
            result[bin] = Math.Clamp((int)(Math.Log(Math.Max(40, frequency) / 40) / span * FREQUENCY_BINS), 0, FREQUENCY_BINS - 1);
        }

        return result;
    }

    private static int GetColumn(double time, double duration, int width)
    {
        return Math.Clamp((int)(time / duration * width), 0, width - 1);
    }

    private static double ToSeconds(MediaTime time)
    {
        return (double)time.Numerator / time.Denominator;
    }
}
