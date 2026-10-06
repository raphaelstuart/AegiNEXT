using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisPcmReader(IAudioSampleSource source, Action checkRequest, CancellationToken lifetime)
{
    private AudioSampleBlock? block;
    private long blockStart;
    private MediaTime? expected;
    private bool eof;

    internal float Read(long sample)
    {
        while (block is null || sample >= blockStart + block.FrameCount)
        {
            if (eof)
            {
                return 0;
            }
            checkRequest();
            block = source.Read(lifetime);
            checkRequest();
            if (block is null)
            {
                eof = true;
                return 0;
            }
            if (block.Format != source.Format || expected is { } previous && block.Start < previous)
            {
                throw new InvalidDataException("PCM 格式不一致或时间戳发生重叠、回退。");
            }
            blockStart = block.Start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
            expected = block.Start + new MediaTime(block.FrameCount, WaveformAnalyzer.SAMPLE_RATE);
        }
        if (sample < blockStart)
        {
            return 0;
        }
        var value = block.Samples.Span[(int)(sample - blockStart)];
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException("PCM 包含非有限样本。");
        }
        return value;
    }
}
