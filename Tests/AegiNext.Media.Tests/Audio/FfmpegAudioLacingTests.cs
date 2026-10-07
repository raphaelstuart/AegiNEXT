using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

public sealed class FfmpegAudioLacingTests
{
    /// <summary>Matroska 同一个 Block 的八个 FLAC 帧保持精确连续 PCM 时间。</summary>
    [AudioFact]
    public async Task EightFrameLacesPreserveContinuous48KhzPcm()
    {
        using var fixture = await FlacLacingFixture.CreateAsync();
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(48000, 1));
        var count = AssertContinuous(decoder, 0);
        Assert.Equal(fixture.SampleCount, count);
    }

    /// <summary>同 Block 连续性不依赖输出重采样率或重采样器延迟。</summary>
    [AudioFact]
    public async Task EightFrameLacesRemainContinuousWhenResampledTo16And44Point1Khz()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(groupCount: 15);
        foreach (var rate in new[] { 16000, 44100 })
        {
            using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(rate, 1));
            var count = AssertContinuous(decoder, 0);
            Assert.Equal(new MediaTime(fixture.SampleCount, FlacLacingFixture.SAMPLE_RATE)
                .ToTimestamp(new(1, rate), MediaTimeRounding.CEILING).Value, count);
        }
    }

    /// <summary>来回定位会重新锚定 Block 时间，保持任意样本目标后的连续输出。</summary>
    [AudioFact]
    public async Task ForwardAndBackwardSeeksClipTheTargetAndKeepFollowingLacesContinuous()
    {
        using var fixture = await FlacLacingFixture.CreateAsync();
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(48000, 1));
        foreach (var target in new[] { new MediaTime(3, 2), new MediaTime(1, 48000), new MediaTime(7, 10), new MediaTime(19, 10) })
        {
            decoder.Seek(target);
            Assert.True(AssertContinuous(decoder,
                target.ToTimestamp(new(1, decoder.Format.SampleRate), MediaTimeRounding.CEILING).Value) > 0);
        }
    }

    /// <summary>音频非零原点与定位后的 Block 识别保留媒体绝对时间。</summary>
    [AudioFact]
    public async Task NonzeroAudioOffsetSurvivesContinuousReadAndSeek()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(offsetMilliseconds: 500);
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(48000, 1));
        decoder.Seek(MediaTime.Zero);
        Assert.Equal(fixture.SampleCount, AssertContinuous(decoder, 24000));
        decoder.Seek(new(7, 4));
        Assert.True(AssertContinuous(decoder, 84000) > 0);
    }

    /// <summary>不同 Block 的真实正向时间间隙继续保留，不能被连续性修复吞掉。</summary>
    [AudioFact]
    public async Task ARealPositiveGapBetweenBlocksKeepsItsMediaTimestamp()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(shiftMilliseconds: 250);
        using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(48000, 1));
        var previousEnd = 0L;
        var gaps = new List<long>();
        var count = 0L;
        while (decoder.Read() is { } block)
        {
            var start = block.Start.ToTimestamp(new(1, 48000), MediaTimeRounding.FLOOR).Value;
            Assert.True(start >= previousEnd, $"PCM 回退：{start} < {previousEnd}。");
            if (start > previousEnd)
            {
                gaps.Add(start - previousEnd);
            }
            previousEnd = start + block.FrameCount;
            count += block.FrameCount;
        }
        Assert.Equal(fixture.SampleCount, count);
        Assert.InRange(Assert.Single(gaps), 12000 - 48, 12000 + 48);
    }

    private static long AssertContinuous(FfmpegAudioDecoder decoder, long firstSample)
    {
        var expected = firstSample;
        var count = 0L;
        double energy = 0;
        while (decoder.Read() is { } block)
        {
            Assert.Equal(new MediaTime(expected, decoder.Format.SampleRate), block.Start);
            Assert.InRange(block.FrameCount, 1, 4096);
            expected += block.FrameCount;
            count += block.FrameCount;
            foreach (var sample in block.Samples.Span)
            {
                Assert.True(float.IsFinite(sample));
                energy += sample * sample;
            }
        }
        Assert.True(count > 0 && energy / count > 0.01);
        return count;
    }
}
