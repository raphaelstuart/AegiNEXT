using AegiNext.Core.Timing;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Controllers;

public sealed partial class VideoPreviewController
{
    /// <summary>
    /// 按原始媒体时间定位并保持用户的播放意图；后来的播放命令使整个旧事务失效。
    /// 定位到媒体结束时保持结束状态，不启动空音频输出。
    /// </summary>
    public Task SeekForPlaybackAsync(MediaTime target, bool resumePlayback)
    {
        return SeekCoreAsync(target, resumePlayback);
    }

    private Task SeekCoreAsync(MediaTime target, bool resumePlayback)
    {
        lock (gate)
        {
            CancelPlaybackRangeUnderLock();
            if (!closed && pendingSeek is { IsCompleted: false } && pendingSeekTarget == target &&
                pendingSeekResumePlayback == resumePlayback && pendingSeekEpoch == epoch &&
                pendingSeekSequence == commandSequence && pendingSeekPresentationRevision == revision)
            {
                return pendingSeek;
            }

            pendingSeek = ExecuteAsync(async (run, session, operationRevision) =>
            {
                await ClearMediaRangeAsync(run, session, operationRevision).ConfigureAwait(false);
                await SubmitAudioCommandAsync(run, operationRevision, audio => audio.PauseAsync()).ConfigureAwait(false);
                Task seek;
                lock (gate)
                {
                    ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                    seek = session.SeekAsync(target);
                }
                await seek.ConfigureAwait(false);
                await SubmitAudioCommandAsync(run, operationRevision,
                    audio => audio.SeekAsync(session.Snapshot.Position)).ConfigureAwait(false);

                lock (gate)
                {
                    ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                    if (!resumePlayback || session.Snapshot.State == VideoPlaybackState.ENDED)
                    {
                        return;
                    }
                }
                await SubmitAudioCommandAsync(run, operationRevision, audio => audio.PlayAsync()).ConfigureAwait(false);
                Task play;
                lock (gate)
                {
                    ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                    play = session.PlayAsync();
                }
                await play.ConfigureAwait(false);
            }, true);
            pendingSeekTarget = target;
            pendingSeekResumePlayback = resumePlayback;
            pendingSeekEpoch = epoch;
            pendingSeekSequence = commandSequence;
            pendingSeekPresentationRevision = revision;
            return pendingSeek;
        }
    }

    private async Task RefreshAfterPendingSeekAsync(VideoPreviewRun run, long requestedRevision, Task seek)
    {
        try
        {
            await seek.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Task refresh;
        lock (gate)
        {
            if (!IsCurrentUnderLock(run) || requestedRevision != revision || run.Error is not null ||
                run.Session is not { } session || session.Snapshot.State != VideoPlaybackState.PAUSED)
            {
                return;
            }
            refresh = SeekAsync(session.Snapshot.Position);
        }
        await refresh.ConfigureAwait(false);
    }
}
