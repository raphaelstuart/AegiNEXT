using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisSourceLifetime : IDisposable
{
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenRegistration registration;
    private int cancelled;
    private int disposed;

    internal AudioAnalysisSourceLifetime(IAudioSampleSource source, CancellationToken cancellationToken)
    {
        Source = source;
        this.cancellationToken = cancellationToken;
        registration = cancellationToken.UnsafeRegister(static state => ((AudioAnalysisSourceLifetime)state!).Cancel(), this);
    }

    internal IAudioSampleSource Source { get; }

    /// <summary>确保取消先于解码源释放，并等待正在执行的取消回调结束。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        if (cancellationToken.IsCancellationRequested)
        {
            Cancel();
        }
        registration.Dispose();
        Source.Dispose();
    }

    private void Cancel()
    {
        if (Interlocked.Exchange(ref cancelled, 1) == 0)
        {
            Source.Cancel();
        }
    }
}
