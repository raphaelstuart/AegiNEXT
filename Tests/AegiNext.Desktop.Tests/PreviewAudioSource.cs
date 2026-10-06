using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewAudioSource : IAudioSampleSource
{
    public AudioSampleFormat Format { get; } = new();
    internal MediaTime LastSeek { get; private set; }
    internal int DisposeCount { get; private set; }

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSeek = target;
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
