using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Playback;

/// <summary>
/// 串行管理视频源、单调播放时钟和有界显示队列；定位完成后暂停，关闭负责终止并回收源。
/// </summary>
public sealed class VideoPlaybackSession : IAsyncDisposable
{
    private readonly Func<CancellationToken, IVideoFrameSource> sourceFactory;
    private readonly TimeProvider timeProvider;
    private readonly long timestampFrequency;
    private readonly int presentationCapacity;
    private readonly Func<MediaTime?>? externalPosition;
    private readonly Lock gate = new();
    private readonly Lock cancellationGate = new();
    private readonly Queue<PlaybackRequest> requests = new();
    private readonly Queue<VideoPresentation> presentations = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private TaskCompletionSource<bool> commandChanged = CreateSignal();
    private TaskCompletionSource<bool> presentationChanged = CreateSignal();
    private VideoPlaybackSnapshot snapshot = new(VideoPlaybackState.CREATED, 0, MediaTime.Zero, null, null);
    private IVideoFrameSource? source;
    private MediaTime clockOriginPosition;
    private long clockOriginTimestamp;
    private MediaTime? nextFrameTime;
    private bool reachedEnd;
    private bool needsResynchronization;
    private bool closeRequested;
    private Task? closeTask;
    private int disposed;

    /// <summary>
    /// 创建尚未打开的会话；工厂与所有帧读取均由单一后台工作循环执行。
    /// </summary>
    public VideoPlaybackSession(
        Func<CancellationToken, IVideoFrameSource> sourceFactory,
        TimeProvider? timeProvider = null,
        int presentationCapacity = 2,
        Func<MediaTime?>? externalPosition = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationCapacity);
        this.sourceFactory = sourceFactory;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        timestampFrequency = this.timeProvider.TimestampFrequency;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timestampFrequency);
        this.presentationCapacity = presentationCapacity;
        this.externalPosition = externalPosition;
        worker = Task.Run(RunWorkerAsync);
    }

    public VideoPlaybackSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot with { Position = GetPositionUnderLock() };
            }
        }
    }

    /// <summary>
    /// 打开源并交付首帧后暂停；取消令牌仅撤回尚未执行的命令，执行开始后由关闭操作终止。
    /// </summary>
    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        return Submit(PlaybackOperation.OPEN, MediaTime.Zero, cancellationToken);
    }

    /// <summary>
    /// 打开本地视频文件并返回已交付首帧的暂停会话；失败时等待全部会话资源回收。
    /// </summary>
    public static async Task<VideoPlaybackSession> OpenAsync(
        string filePath,
        int videoStreamIndex,
        TimeProvider? timeProvider = null,
        int presentationCapacity = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(videoStreamIndex);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(filePath);
        var session = new VideoPlaybackSession(token => VideoFrameNavigator.Open(path, videoStreamIndex, token), timeProvider, presentationCapacity);
        try
        {
            await session.OpenAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 从当前媒体位置按原速播放；已结束的源保持结束，重新播放需先定位。
    /// </summary>
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        return Submit(PlaybackOperation.PLAY, MediaTime.Zero, cancellationToken);
    }

    /// <summary>
    /// 冻结提交命令时的媒体位置并定位暂停帧；已开始的源操作不会被命令取消令牌中断。
    /// </summary>
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        return Submit(PlaybackOperation.PAUSE, MediaTime.Zero, cancellationToken);
    }

    /// <summary>
    /// 定位半开区间内的帧并暂停；较新的暂停或定位会使尚未交付的旧结果失效。
    /// </summary>
    public async Task<VideoSeekResult> SeekAsync(MediaTime target, CancellationToken cancellationToken = default)
    {
        return (await Submit(PlaybackOperation.SEEK, target, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// 等待并接管显示帧；正常结束或关闭返回 null，源失败传播异常，读取取消不影响会话。
    /// </summary>
    public async ValueTask<VideoPresentation?> ReadPresentationAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task changed;
            lock (gate)
            {
                if (snapshot.Error is { } error)
                {
                    throw new InvalidOperationException("视频播放会话已失败。", error);
                }

                while (presentations.TryDequeue(out var presentation))
                {
                    var expiredWhilePlaying = snapshot.State == VideoPlaybackState.PLAYING &&
                        presentation.PositionedFrame.NextFrameTime is { } next && next <= GetPositionUnderLock();
                    var obsoleteAtEnd = snapshot.State == VideoPlaybackState.ENDED &&
                        presentation.PositionedFrame.Time != snapshot.DisplayTime;
                    if (!expiredWhilePlaying && !obsoleteAtEnd)
                    {
                        return presentation;
                    }

                    presentation.Dispose();
                }

                if (closeRequested || snapshot.State is VideoPlaybackState.CLOSED or VideoPlaybackState.ENDED)
                {
                    return null;
                }

                changed = presentationChanged.Task;
            }

            await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 终止源操作，等待工作循环退出并释放尚未交付的帧；可并发、重复调用。
    /// </summary>
    public Task CloseAsync()
    {
        lock (cancellationGate)
        {
            IVideoFrameSource? activeSource;
            lock (gate)
            {
                if (closeRequested)
                {
                    return closeTask ?? worker;
                }

                closeRequested = true;
                activeSource = source;
                ClearPresentationsUnderLock();
                PulseCommandUnderLock();
                PulsePresentationUnderLock();
            }

            Exception? failure = null;
            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                failure = error;
            }

            try
            {
                activeSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception error)
            {
                failure = failure is null ? error : new AggregateException(failure, error);
            }

            closeTask = CompleteCloseAsync(failure);
            return closeTask;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (cancellationGate)
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    lifetime.Dispose();
                }
            }
        }
    }

    private Task<VideoSeekResult?> Submit(PlaybackOperation operation, MediaTime target, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<VideoSeekResult?>(cancellationToken);
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closeRequested, this);
            if (snapshot.Error is { } error)
            {
                return Task.FromException<VideoSeekResult?>(new InvalidOperationException("视频播放会话已失败。", error));
            }

            if (operation == PlaybackOperation.OPEN)
            {
                if (snapshot.State != VideoPlaybackState.CREATED)
                {
                    throw new InvalidOperationException("视频播放会话已经开始打开。");
                }

                snapshot = snapshot with { State = VideoPlaybackState.OPENING };
            }
            else if (snapshot.State is VideoPlaybackState.CREATED or VideoPlaybackState.OPENING)
            {
                throw new InvalidOperationException("视频播放会话尚未打开。");
            }

            if (operation is PlaybackOperation.PAUSE or PlaybackOperation.SEEK)
            {
                if (operation == PlaybackOperation.PAUSE)
                {
                    target = GetPositionUnderLock();
                }

                snapshot = snapshot with { Generation = checked(snapshot.Generation + 1) };
                ClearPresentationsUnderLock();
            }

            var request = new PlaybackRequest(operation, snapshot.Generation, target, cancellationToken);
            requests.Enqueue(request);
            PulseCommandUnderLock();
            return request.Completion;
        }
    }

    private async Task RunWorkerAsync()
    {
        PlaybackRequest? activeRequest = null;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                Task changed;
                MediaTime? due;
                long generation;
                lock (gate)
                {
                    requests.TryDequeue(out activeRequest);
                    changed = commandChanged.Task;
                    generation = snapshot.Generation;
                    due = snapshot.State == VideoPlaybackState.PLAYING ? nextFrameTime - GetPositionUnderLock() : null;
                }

                if (activeRequest is not null)
                {
                    using (activeRequest)
                    {
                        if (activeRequest.TryStart())
                        {
                            ExecuteRequest(activeRequest);
                        }
                        else if (activeRequest.Operation == PlaybackOperation.OPEN)
                        {
                            lock (gate)
                            {
                                snapshot = snapshot with { State = VideoPlaybackState.CREATED };
                            }
                        }
                        else if (activeRequest.Operation is PlaybackOperation.PAUSE or PlaybackOperation.SEEK)
                        {
                            bool restore;
                            lock (gate)
                            {
                                restore = !closeRequested && activeRequest.Generation == snapshot.Generation;
                                needsResynchronization |= restore;
                            }

                            if (restore)
                            {
                                AdvancePlayback(activeRequest.Generation);
                            }
                        }
                    }

                    activeRequest = null;
                    continue;
                }

                if (due is { } remaining)
                {
                    if (needsResynchronization || remaining <= MediaTime.Zero)
                    {
                        AdvancePlayback(generation);
                        continue;
                    }

                    await WaitForCommandOrTimeAsync(changed, remaining).ConfigureAwait(false);
                }
                else
                {
                    await changed.WaitAsync(lifetime.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            activeRequest?.Supersede();
        }
        catch (Exception error)
        {
            SetFault(error);
            activeRequest?.Fail(error);
        }
        finally
        {
            activeRequest?.Dispose();
            IVideoFrameSource? ownedSource;
            lock (gate)
            {
                ownedSource = source;
                source = null;
            }

            try
            {
                ownedSource?.Dispose();
            }
            catch (Exception error)
            {
                SetFault(error);
            }

            lock (gate)
            {
                while (requests.TryDequeue(out var request))
                {
                    if (snapshot.Error is { } error)
                    {
                        request.Fail(error);
                    }
                    else
                    {
                        request.Supersede();
                    }

                    request.Dispose();
                }

                ClearPresentationsUnderLock();
                if (closeRequested)
                {
                    snapshot = snapshot with { State = VideoPlaybackState.CLOSED, Position = GetPositionUnderLock() };
                }

                PulsePresentationUnderLock();
            }
        }
    }

    private void ExecuteRequest(PlaybackRequest request)
    {
        lock (gate)
        {
            if (closeRequested || request.Generation != snapshot.Generation)
            {
                request.Supersede();
                return;
            }
        }

        if (request.Operation == PlaybackOperation.OPEN)
        {
            var openedSource = sourceFactory(lifetime.Token) ?? throw new InvalidDataException("视频源工厂返回了空实例。");
            lock (gate)
            {
                source = openedSource;
                if (closeRequested)
                {
                    request.Supersede();
                    return;
                }
            }

            var first = openedSource.ReadFrame(lifetime.Token);
            CommitPosition(request, first, first?.Time ?? MediaTime.Zero);
            return;
        }

        if (request.Operation == PlaybackOperation.PLAY)
        {
            lock (gate)
            {
                if (closeRequested || request.Generation != snapshot.Generation)
                {
                    request.Supersede();
                    return;
                }

                if (snapshot.State != VideoPlaybackState.PLAYING)
                {
                    clockOriginPosition = snapshot.Position;
                    clockOriginTimestamp = timeProvider.GetTimestamp();
                    snapshot = snapshot with { State = reachedEnd ? VideoPlaybackState.ENDED : VideoPlaybackState.PLAYING };
                    PulsePresentationUnderLock();
                }

                request.Complete();
            }

            return;
        }

        try
        {
            var frame = source!.SeekFrame(request.Target, () => IsSuperseded(request.Generation), lifetime.Token);
            CommitPosition(request, frame, request.Target);
        }
        catch (Exception) when (IsSuperseded(request.Generation))
        {
            needsResynchronization = true;
            request.Supersede();
        }
    }

    private void CommitPosition(PlaybackRequest request, PositionedVideoFrame? frame, MediaTime requestedPosition)
    {
        try
        {
            ValidateFrame(frame);
            lock (gate)
            {
                if (closeRequested || request.Generation != snapshot.Generation)
                {
                    needsResynchronization = true;
                    request.Supersede();
                    return;
                }

                var position = frame is { IsBeforeFirst: true } or { ReachedEnd: true } ? frame.Time : requestedPosition;
                reachedEnd = frame is null || frame.ReachedEnd;
                nextFrameTime = frame?.NextFrameTime;
                needsResynchronization = false;
                snapshot = snapshot with
                {
                    State = frame is null ? VideoPlaybackState.ENDED : VideoPlaybackState.PAUSED,
                    Position = position,
                    DisplayTime = frame?.Time
                };
                var result = new VideoSeekResult(requestedPosition, frame?.Time, frame?.NextFrameTime, request.Generation,
                    frame?.IsBeforeFirst ?? false, reachedEnd);
                if (frame is not null)
                {
                    PublishUnderLock(frame, request.Generation);
                    frame = null;
                }

                PulsePresentationUnderLock();
                request.Complete(request.Operation == PlaybackOperation.SEEK ? result : null);
            }
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private void AdvancePlayback(long generation)
    {
        PositionedVideoFrame? frame = null;
        try
        {
            MediaTime position;
            lock (gate)
            {
                position = GetPositionUnderLock();
            }

            frame = needsResynchronization
                ? source!.SeekFrame(position, () => IsSuperseded(generation), lifetime.Token)
                : source!.ReadFrame(lifetime.Token);
            while (true)
            {
                ValidateFrame(frame);
                lock (gate)
                {
                    if (closeRequested || generation != snapshot.Generation)
                    {
                        needsResynchronization = true;
                        return;
                    }

                    position = GetPositionUnderLock();
                    if (frame is null)
                    {
                        reachedEnd = true;
                        nextFrameTime = null;
                        snapshot = snapshot with { State = VideoPlaybackState.ENDED, Position = snapshot.DisplayTime ?? snapshot.Position };
                        PulsePresentationUnderLock();
                        return;
                    }

                    if (frame.NextFrameTime is null || frame.NextFrameTime > position)
                    {
                        reachedEnd = frame.ReachedEnd;
                        nextFrameTime = frame.NextFrameTime;
                        needsResynchronization = false;
                        snapshot = snapshot with { DisplayTime = frame.Time };
                        if (reachedEnd && snapshot.State == VideoPlaybackState.PLAYING)
                        {
                            snapshot = snapshot with { State = VideoPlaybackState.ENDED, Position = frame.Time };
                        }

                        PublishUnderLock(frame, generation);
                        frame = null;
                        PulsePresentationUnderLock();
                        return;
                    }
                }

                frame.Dispose();
                frame = source!.ReadFrame(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (IsSuperseded(generation))
        {
            needsResynchronization = true;
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private async Task WaitForCommandOrTimeAsync(Task command, MediaTime remaining)
    {
        var maximumDelay = MediaTime.FromTimeSpan(TimeSpan.FromDays(1));
        var bounded = remaining < maximumDelay ? remaining : maximumDelay;
        using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var delay = Task.Delay(bounded.ToTimeSpan(MediaTimeRounding.CEILING), timeProvider, delayCancellation.Token);
        await Task.WhenAny(command, delay).ConfigureAwait(false);
        await delayCancellation.CancelAsync().ConfigureAwait(false);
    }

    private bool IsSuperseded(long generation)
    {
        lock (gate)
        {
            return closeRequested || generation != snapshot.Generation;
        }
    }

    private async Task CompleteCloseAsync(Exception? error)
    {
        try
        {
            await worker.ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                snapshot = snapshot with { State = VideoPlaybackState.CLOSED, Position = GetPositionUnderLock() };
                PulsePresentationUnderLock();
            }
        }

        if (error is not null)
        {
            await Task.FromException(error).ConfigureAwait(false);
        }
    }

    private void SetFault(Exception error)
    {
        lock (gate)
        {
            snapshot = snapshot with { State = VideoPlaybackState.FAULTED, Position = GetPositionUnderLock(), Error = error };
            ClearPresentationsUnderLock();
            PulsePresentationUnderLock();
        }
    }

    private MediaTime GetPositionUnderLock()
    {
        if (snapshot.State != VideoPlaybackState.PLAYING)
        {
            return snapshot.Position;
        }

        if (externalPosition?.Invoke() is { } position)
        {
            clockOriginPosition = position;
            clockOriginTimestamp = timeProvider.GetTimestamp();
            return position;
        }

        var elapsed = unchecked(timeProvider.GetTimestamp() - clockOriginTimestamp);
        if (elapsed < 0)
        {
            throw new InvalidOperationException("播放时钟必须单调递增。");
        }

        return clockOriginPosition + new MediaTime(elapsed, timestampFrequency);
    }

    private void PublishUnderLock(PositionedVideoFrame frame, long generation)
    {
        while (presentations.Count >= presentationCapacity)
        {
            presentations.Dequeue().Dispose();
        }

        presentations.Enqueue(new(frame, generation));
        PulsePresentationUnderLock();
    }

    private void ClearPresentationsUnderLock()
    {
        while (presentations.TryDequeue(out var presentation))
        {
            presentation.Dispose();
        }
    }

    private void PulseCommandUnderLock()
    {
        var previous = commandChanged;
        commandChanged = CreateSignal();
        previous.TrySetResult(true);
    }

    private void PulsePresentationUnderLock()
    {
        var previous = presentationChanged;
        presentationChanged = CreateSignal();
        previous.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static void ValidateFrame(PositionedVideoFrame? frame)
    {
        if (frame is null)
        {
            return;
        }

        if (frame.NextFrameTime is { } next && next <= frame.Time)
        {
            throw new InvalidDataException("定位帧的下一帧时间必须严格递增。");
        }

        if (frame.ReachedEnd != (frame.NextFrameTime is null))
        {
            throw new InvalidDataException("定位帧的结束标记与下一帧时间不一致。");
        }
    }
}
