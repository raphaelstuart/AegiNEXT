using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Controllers;

public sealed partial class VideoPreviewController
{
    private Func<AudioOutputClockSnapshot, MediaTime>? audioCalibration;

    public AudioOutputClockSnapshot? AudioClock
    {
        get
        {
            lock (gate)
            {
                return opening ? null : current?.Audio?.ClockSnapshot;
            }
        }
    }

    /// <summary>配置新音频会话使用的设备校准提供方；回调必须线程安全且不能修改工程。</summary>
    public void ConfigureAudioCalibration(Func<AudioOutputClockSnapshot, MediaTime>? provider)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            audioCalibration = provider;
            if (current?.Audio is { } audio)
            {
                audio.ConfigureCalibration(provider);
            }
        }
    }

    /// <summary>暂停失效输出并重新打开当前设备，在原始媒体位置重建时钟与视频帧代次。</summary>
    public Task ReopenAudioOutputAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async (run, session, operationRevision) =>
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(run.Token, cancellationToken);
            cancellation.Token.ThrowIfCancellationRequested();
            var target = run.Audio?.Position ?? session.Snapshot.Position;
            await session.PauseAsync().ConfigureAwait(false);
            if (run.Audio is { } previous)
            {
                await previous.DisposeAsync().ConfigureAwait(false);
            }
            if (audioFactory is null || run.Media?.AudioStreamIndex is not { } index)
            {
                throw new InvalidOperationException("当前媒体没有可重建的音频输出。");
            }
            var replacement = await audioFactory(run.Path, index, target, cancellation.Token).ConfigureAwait(false);
            var attached = false;
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                lock (gate)
                {
                    ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                    replacement.ConfigureCalibration(audioCalibration);
                    replacement.SetGain(muted ? 0 : volume);
                    run.AttachAudio(replacement);
                    run.AudioError = null;
                    attached = true;
                }
                await session.SeekAsync(target).ConfigureAwait(false);
                await replacement.SeekAsync(session.Snapshot.Position).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            finally
            {
                if (!attached)
                {
                    await replacement.DisposeAsync().ConfigureAwait(false);
                }
            }
        }, true);
    }
}
