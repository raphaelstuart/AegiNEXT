using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class PreviewRangeAudioSource : IAudioSampleSource
{
    private long nextSample;

    public AudioSampleFormat Format { get; } = new();
    internal MediaTime LastSeek { get; private set; }
    internal Action<MediaTime>? BeforeSeek { get; set; }

    public AudioSampleBlock Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var samples = new float[256];
        for (var index = 0; index < 128; index++)
        {
            samples[index * 2] = nextSample + index;
            samples[index * 2 + 1] = nextSample + index;
        }
        var block = new AudioSampleBlock(Format, new(nextSample, 48000), samples);
        nextSample += 128;
        return block;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeSeek?.Invoke(target);
        LastSeek = target;
        nextSample = target.ToTimestamp(new(1, 48000), MediaTimeRounding.FLOOR).Value;
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
    }
}
