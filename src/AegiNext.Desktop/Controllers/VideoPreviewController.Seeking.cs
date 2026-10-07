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

    /// <summary>交互定位先交付暂停帧再恢复播放，让下一轮拖动不会取消尚未完成的转换。</summary>
    internal Task SeekForInteractivePlaybackAsync(MediaTime target, bool resumePlayback)
    {
        return SeekCoreAsync(target, resumePlayback, true);
    }

    private Task SeekCoreAsync(MediaTime target, bool resumePlayback, bool waitForPresentation = false)
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
                var presentationRevision = revision;
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

                if (waitForPresentation)
                {
                    await WaitForSeekPresentationAsync(run, session, operationRevision, presentationRevision).ConfigureAwait(false);
                }

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

    private async Task WaitForSeekPresentationAsync(VideoPreviewRun run, VideoPlaybackSession session, long operationRevision,
        long presentationRevision)
    {
        while (true)
        {
            Task changed;
            lock (gate)
            {
                ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                if (run.Error is { } error)
                {
                    throw new InvalidOperationException("视频预览转换失败。", error);
                }
                var snapshot = session.Snapshot;
                if (presentationRevision != revision || snapshot.DisplayTime is null || run.PresentedGeneration == snapshot.Generation)
                {
                    return;
                }
                changed = run.PresentationChanged.Task;
            }
            await changed.WaitAsync(run.Token).ConfigureAwait(false);
        }
    }

    private static void PulsePresentationUnderLock(VideoPreviewRun run)
    {
        var changed = run.PresentationChanged;
        run.PresentationChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
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
