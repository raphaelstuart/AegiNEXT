using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Controllers;

public sealed partial class VideoPreviewController
{
    private CancellationTokenSource? rangeCancellation;
    private Task rangeWorker = Task.CompletedTask;
    private Task rangeStop = Task.CompletedTask;
    private long rangeRevision;
    private bool mediaRangeInstalled;
    private bool rangeLoop;

    /// <summary>当前媒体是否仍由字幕范围播放任务拥有；主窗口定位和暂停会立即撤销。</summary>
    public bool IsRangePlaybackActive
    {
        get
        {
            lock (gate)
            {
                return rangeCancellation is { IsCancellationRequested: false };
            }
        }
    }

    /// <summary>在原始媒体时间范围内手动开始播放；首次开始后返回，循环由后台拥有者协调。</summary>
    public async Task PlayRangeAsync(MediaTime start, MediaTime end, bool loop, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = new MediaTimeRange(start, end);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var run = current ?? throw new InvalidOperationException("视频尚未打开。");
            if (opening || run.Error is not null || run.Session is null || run.Media is null)
            {
                throw new InvalidOperationException("视频尚未就绪。");
            }
            var lower = run.Media.Start ?? MediaTime.Zero;
            var upper = run.Media.Duration is { } duration ? lower + duration : requested.End;
            start = requested.Start > lower ? requested.Start : lower;
            end = requested.End < upper ? requested.End : upper;
            var range = new MediaTimeRange(start, end);
            CancelPlaybackRangeUnderLock();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Token);
            rangeCancellation = cancellation;
            rangeLoop = loop;
            var owner = ++rangeRevision;
            rangeWorker = RunPlaybackRangeAsync(run, range, owner, cancellation, started);
        }
        await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>调整当前段内播放的循环开关；暂停后调用不会开始播放或重新定位。</summary>
    public void SetPlaybackRangeLoop(bool loop)
    {
        lock (gate)
        {
            if (rangeCancellation is { IsCancellationRequested: false })
            {
                rangeLoop = loop;
            }
        }
    }

    /// <summary>立即撤销循环拥有者并暂停，等待旧任务排空后解除媒体范围。</summary>
    public async Task ClearPlaybackRangeAsync()
    {
        Task worker;
        Task stop;
        long owner;
        VideoPreviewRun? run;
        lock (gate)
        {
            CancelPlaybackRangeUnderLock();
            worker = rangeWorker;
            stop = rangeStop;
            owner = rangeRevision;
            run = current;
        }
        await Task.WhenAll(worker, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (gate)
        {
            if (closed || opening || owner != rangeRevision || run is null || !IsCurrentUnderLock(run) || run.Session is null)
            {
                return;
            }
        }
        await ExecuteAsync(async (ready, session, operationRevision) =>
        {
            lock (gate)
            {
                if (owner != rangeRevision)
                {
                    return;
                }
                ThrowIfCommandObsoleteUnderLock(ready, operationRevision);
            }
            await ClearMediaRangeAsync(ready, session, operationRevision).ConfigureAwait(false);
        }, true).ConfigureAwait(false);
    }

    private async Task RunPlaybackRangeAsync(VideoPreviewRun run, MediaTimeRange range, long owner,
        CancellationTokenSource cancellation, TaskCompletionSource started)
    {
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                await ExecuteAsync(async (ready, session, operationRevision) =>
                {
                    Task installAudio;
                    lock (gate)
                    {
                        RequireRangeOwnerUnderLock(run, owner, token);
                        ThrowIfCommandObsoleteUnderLock(ready, operationRevision);
                        mediaRangeInstalled = true;
                        session.SetPlaybackRange(range);
                        installAudio = SubmitAudioCommandAsync(ready, operationRevision, audio => audio.SetPlaybackRangeAsync(range));
                    }
                    await installAudio.ConfigureAwait(false);
                    Task seek;
                    lock (gate)
                    {
                        RequireRangeOwnerUnderLock(run, owner, token);
                        ThrowIfCommandObsoleteUnderLock(ready, operationRevision);
                        seek = session.SeekAsync(range.Start, token);
                    }
                    await seek.ConfigureAwait(false);
                    Task audioPlay;
                    lock (gate)
                    {
                        RequireRangeOwnerUnderLock(run, owner, token);
                        audioPlay = SubmitAudioCommandAsync(ready, operationRevision, audio => audio.PlayAsync());
                    }
                    await audioPlay.ConfigureAwait(false);
                    Task play;
                    lock (gate)
                    {
                        RequireRangeOwnerUnderLock(run, owner, token);
                        ThrowIfCommandObsoleteUnderLock(ready, operationRevision);
                        play = session.PlayAsync(token);
                    }
                    await play.ConfigureAwait(false);
                }, true).ConfigureAwait(false);
                started.TrySetResult();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    lock (gate)
                    {
                        RequireRangeOwnerUnderLock(run, owner, token);
                    }
                    if (run.Session!.Snapshot.State == VideoPlaybackState.ENDED || run.Audio is { ReachedPlaybackRangeEnd: true })
                    {
                        break;
                    }
                    await Task.Delay(5, token).ConfigureAwait(false);
                }
                lock (gate)
                {
                    RequireRangeOwnerUnderLock(run, owner, token);
                    if (!rangeLoop)
                    {
                        break;
                    }
                }
            }
            var finish = Task.CompletedTask;
            lock (gate)
            {
                RequireRangeOwnerUnderLock(run, owner, token);
                if (run.Audio is { } finishedAudio)
                {
                    finish = TryAudioAsync(run, finishedAudio.PauseAsync);
                }
            }
            await finish.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            started.TrySetCanceled(token.IsCancellationRequested ? token : new(true));
        }
        catch (ObjectDisposedException)
        {
            started.TrySetCanceled(new CancellationToken(true));
        }
        catch (Exception error)
        {
            started.TrySetException(error);
            lock (gate)
            {
                if (IsCurrentUnderLock(run) && owner == rangeRevision)
                {
                    run.Error = error;
                }
            }
        }
        finally
        {
            var stop = Task.CompletedTask;
            long? completionRevision = null;
            lock (gate)
            {
                if (!closed && owner == rangeRevision && IsCurrentUnderLock(run) && !run.Token.IsCancellationRequested && run.Session is { } session)
                {
                    stop = Task.WhenAll(run.Audio is { } audio ? TryAudioAsync(run, audio.PauseAsync) : Task.CompletedTask,
                        session.Snapshot.State == VideoPlaybackState.PLAYING ? PauseRangeSessionAsync(session) : Task.CompletedTask);
                }
                if (ReferenceEquals(rangeCancellation, cancellation))
                {
                    rangeCancellation = null;
                    completionRevision = revision;
                }
            }
            await stop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellation.Dispose();
            if (completionRevision is { } completed)
            {
                try
                {
                    await DispatchAsync(run, null, null, false, run.Token, completed).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
                {
                }
            }
        }
    }

    private void RequireRangeOwnerUnderLock(VideoPreviewRun run, long owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closed || owner != rangeRevision || !IsCurrentUnderLock(run))
        {
            throw new OperationCanceledException("字幕范围播放已被新的操作替换。", token);
        }
    }

    private void CancelPlaybackRangeUnderLock()
    {
        if (rangeCancellation is null && !mediaRangeInstalled)
        {
            return;
        }
        rangeCancellation?.Cancel();
        rangeCancellation = null;
        rangeRevision++;
        revision++;
        pendingSeek = null;
        if (current is { Token.IsCancellationRequested: false, Session: { } session } run &&
            session.Snapshot.State is VideoPlaybackState.PAUSED or VideoPlaybackState.PLAYING or VideoPlaybackState.ENDED)
        {
            rangeStop = Task.WhenAll(run.Audio is { } audio ? TryAudioAsync(run, audio.PauseAsync) : Task.CompletedTask,
                PauseRangeSessionAsync(session));
        }
    }

    private async Task ClearMediaRangeAsync(VideoPreviewRun run, VideoPlaybackSession session, long operationRevision)
    {
        Task clearAudio;
        lock (gate)
        {
            ThrowIfCommandObsoleteUnderLock(run, operationRevision);
            if (!mediaRangeInstalled)
            {
                return;
            }
            mediaRangeInstalled = false;
            session.SetPlaybackRange(null);
            clearAudio = SubmitAudioCommandAsync(run, operationRevision, audio => audio.SetPlaybackRangeAsync(null));
        }
        await clearAudio.ConfigureAwait(false);
    }

    private Task SubmitAudioCommandAsync(VideoPreviewRun run, long operationRevision, Func<AudioPlaybackSession, Task> command)
    {
        lock (gate)
        {
            ThrowIfCommandObsoleteUnderLock(run, operationRevision);
            return run.AudioError is null && run.Audio is { Error: null } audio
                ? TryAudioAsync(run, () => command(audio)) : Task.CompletedTask;
        }
    }

    private static async Task PauseRangeSessionAsync(VideoPlaybackSession session)
    {
        await session.PauseAsync().ConfigureAwait(false);
    }
}
