using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisPcmReader(IAudioSampleSource source, Action checkRequest, CancellationToken lifetime)
{
    private ReadOnlyMemory<float> samples;
    private readonly float[]? readBuffer = source is FfmpegAudioDecoder ? new float[4096] : null;
    private bool hasBlock;
    private long blockStart;
    private MediaTime? expected;
    private long? lastBlockEnd;
    private bool eof;
    internal long? EndSample { get; private set; }

    internal void CopyTo(long sample, Span<float> destination)
    {
        while (!destination.IsEmpty)
        {
            checkRequest();
            if (!Prepare(sample))
            {
                destination.Clear();
                return;
            }
            if (sample < blockStart)
            {
                var silent = (int)Math.Min(destination.Length, blockStart - sample);
                destination[..silent].Clear();
                destination = destination[silent..];
                sample += silent;
                continue;
            }
            var count = (int)Math.Min(Math.Min(destination.Length, 1024), blockStart + samples.Length - sample);
            var part = samples.Span.Slice((int)(sample - blockStart), count);
            AudioAnalysisSampleValidation.EnsureFinite(part);
            part.CopyTo(destination);
            destination = destination[count..];
            sample += count;
        }
    }

    private bool Prepare(long sample)
    {
        while (!hasBlock || sample >= blockStart + samples.Length)
        {
            if (eof)
            {
                return false;
            }
            checkRequest();
            MediaTime start;
            if (source is FfmpegAudioDecoder decoder)
            {
                var count = decoder.ReadInto(readBuffer!, out start, lifetime);
                samples = readBuffer.AsMemory(0, count);
            }
            else if (source.Read(lifetime) is { } block)
            {
                if (block.Format != source.Format)
                {
                    throw new InvalidDataException("PCM 格式与分析源声明不一致。");
                }
                start = block.Start;
                samples = block.Samples;
            }
            else
            {
                start = default;
                samples = default;
            }
            checkRequest();
            if (samples.IsEmpty)
            {
                eof = true;
                EndSample = lastBlockEnd ?? sample;
                return false;
            }
            if (expected is { } previous && start < previous)
            {
                throw new InvalidDataException("PCM 格式不一致或时间戳发生重叠、回退。");
            }
            hasBlock = true;
            blockStart = start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            lastBlockEnd = blockStart + samples.Length;
            expected = start + new MediaTime(samples.Length, WaveformAnalyzer.SAMPLE_RATE);
        }
        return true;
    }
}
