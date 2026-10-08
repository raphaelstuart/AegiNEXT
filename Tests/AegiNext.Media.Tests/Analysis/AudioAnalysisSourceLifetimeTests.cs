using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisSourceLifetimeTests
{
    [Fact]
    public void ReleasingAnUncancelledScanDoesNotCancelTheDecoder()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new WindowAudioSource(_ => 0, 1);
        var scan = new AudioAnalysisSourceLifetime(source, lifetime.Token);
        scan.Dispose();
        lifetime.Cancel();
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void CancellationAndRepeatedReleaseNotifyAndDisposeTheDecoderOnce()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new WindowAudioSource(_ => 0, 1);
        using var scan = new AudioAnalysisSourceLifetime(source, lifetime.Token);
        lifetime.Cancel();
        scan.Dispose();
        scan.Dispose();
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public void ReadCompletionCanReleaseTheScanBeforeItsCancellationCallbackRuns()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new WindowAudioSource(_ => 0, 1);
        using var scan = new AudioAnalysisSourceLifetime(source, lifetime.Token);
        using var readCompletion = lifetime.Token.UnsafeRegister(static state =>
            ((AudioAnalysisSourceLifetime)state!).Dispose(), scan);
        lifetime.Cancel();
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }
}
