using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Analysis;

internal sealed class AnalysisAudioSource(IEnumerable<AudioSampleBlock> blocks) : IAudioSampleSource
{
    private readonly Queue<AudioSampleBlock> blocks = new(blocks);
    public AudioSampleFormat Format { get; init; } = new(16000, 1);
    internal Action? BeforeRead { get; init; }
    internal int DisposeCount { get; private set; }

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeRead?.Invoke();
        return blocks.TryDequeue(out var block) ? block : null;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
