using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisPcmReader(IAudioSampleSource source, Action checkRequest, CancellationToken lifetime)
{
    private AudioSampleBlock? block;
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
            var count = (int)Math.Min(Math.Min(destination.Length, 1024), blockStart + block!.FrameCount - sample);
            var samples = block.Samples.Span.Slice((int)(sample - blockStart), count);
            AudioAnalysisSampleValidation.EnsureFinite(samples);
            samples.CopyTo(destination);
            destination = destination[count..];
            sample += count;
        }
    }

    private bool Prepare(long sample)
    {
        while (block is null || sample >= blockStart + block.FrameCount)
        {
            if (eof)
            {
                return false;
            }
            checkRequest();
            block = source.Read(lifetime);
            checkRequest();
            if (block is null)
            {
                eof = true;
                EndSample = lastBlockEnd ?? sample;
                return false;
            }
            if (block.Format != source.Format || expected is { } previous && block.Start < previous)
            {
                throw new InvalidDataException("PCM 格式不一致或时间戳发生重叠、回退。");
            }
            blockStart = block.Start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            lastBlockEnd = blockStart + block.FrameCount;
            expected = block.Start + new MediaTime(block.FrameCount, WaveformAnalyzer.SAMPLE_RATE);
        }
        return true;
    }
}
