using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controllers;

/// <summary>
/// 连接媒体会话与 UI；转换和交付均串行，文件身份与播放代数共同拒绝过时画面。
/// </summary>
public sealed partial class VideoPreviewController : IAsyncDisposable
{
    private readonly Func<string, CancellationToken, Task<VideoPreviewMedia>> probe;
    private readonly Func<string, int, Func<MediaTime?>?, VideoDecoderOptions, VideoPlaybackSession> sessionFactory;
    private readonly Func<string, int, MediaTime, CancellationToken, Task<AudioPlaybackSession>>? audioFactory;
    private readonly Func<IVideoPreviewConverter> converterFactory;
    private readonly Func<Action, CancellationToken, Task> dispatch;
    private readonly Action<VideoPreviewUpdate> present;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly SemaphoreSlim dispatchGate = new(1, 1);
    private VideoPreviewRun? current;
    private string? requestedPath;
    private long epoch;
    private long revision;
    private long commandSequence;
    private Task? pendingSeek;
    private MediaTime pendingSeekTarget;
    private long pendingSeekEpoch;
    private long pendingSeekSequence;
    private bool opening;
    private bool closed;
    private Task? closeTask;
    private TaskCompletionSource<bool>? operationsDrained;
    private int activeOperations;
    private int disposed;
    private float volume = 1;
    private bool muted;
    private VideoDecodeMode decodeMode;
    private bool switchingDecodeMode;

    /// <summary>
    /// 使用真实探测、解码与 SDR 转换服务创建控制器；UI 调度须返回可等待的完成任务。
    /// 画面回调必须快速完成，不可同步等待此控制器的异步操作。
    /// </summary>
    public VideoPreviewController(Func<Action, CancellationToken, Task> dispatch, Action<VideoPreviewUpdate> present,
        Func<IVideoPreviewConverter>? converterFactory = null)
        : this(VideoPreviewProbe.ProbeAsync,
            (path, index, clock, options) => new(token => VideoFrameNavigator.Open(path, index, options, token), externalPosition: clock),
            converterFactory ?? (static () => new SdrVideoConverter()), dispatch, present, AudioPlaybackSession.OpenAsync)
    {
    }

    /// <summary>
    /// 注入不依赖窗口系统的服务；会话工厂必须返回尚未打开的新实例。
    /// 画面回调必须快速完成，不可同步等待此控制器的异步操作。
    /// </summary>
    public VideoPreviewController(
        Func<string, CancellationToken, Task<VideoPreviewMedia>> probe,
        Func<string, int, VideoPlaybackSession> sessionFactory,
        Func<IVideoPreviewConverter> converterFactory,
        Func<Action, CancellationToken, Task> dispatch,
        Action<VideoPreviewUpdate> present)
        : this(probe, (path, index, _, _) => sessionFactory(path, index), converterFactory, dispatch, present)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
    }

    /// <summary>
    /// 注入影音会话和音频主时钟；音频创建失败通过快照公开，视频保持可用。
    /// </summary>
    public VideoPreviewController(
        Func<string, CancellationToken, Task<VideoPreviewMedia>> probe,
        Func<string, int, Func<MediaTime?>?, VideoPlaybackSession> sessionFactory,
        Func<IVideoPreviewConverter> converterFactory,
        Func<Action, CancellationToken, Task> dispatch,
        Action<VideoPreviewUpdate> present,
        Func<string, int, MediaTime, CancellationToken, Task<AudioPlaybackSession>>? audioFactory)
        : this(probe, (path, index, clock, _) => sessionFactory(path, index, clock), converterFactory, dispatch, present, audioFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
    }

    /// <summary>注入能接收解码选项的会话工厂，以同一工作流打开和切换实际解码后端。</summary>
    public VideoPreviewController(
        Func<string, CancellationToken, Task<VideoPreviewMedia>> probe,
        Func<string, int, Func<MediaTime?>?, VideoDecoderOptions, VideoPlaybackSession> sessionFactory,
        Func<IVideoPreviewConverter> converterFactory,
        Func<Action, CancellationToken, Task> dispatch,
        Action<VideoPreviewUpdate> present,
        Func<string, int, MediaTime, CancellationToken, Task<AudioPlaybackSession>>? audioFactory = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(converterFactory);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(present);
        this.probe = probe;
        this.sessionFactory = sessionFactory;
        this.audioFactory = audioFactory;
        this.converterFactory = converterFactory;
        this.dispatch = dispatch;
        this.present = present;
    }

    /// <summary>取得当前生效的解码类型；媒体尚未打开时用于下一次打开。</summary>
    public VideoDecodeMode DecodeMode
    {
        get
        {
            lock (gate)
            {
                return decodeMode;
            }
        }
    }

    internal void ConfigureDecodeMode(VideoDecodeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (requestedPath is not null || opening)
            {
                throw new InvalidOperationException("打开媒体后必须通过切换工作流更改解码类型。");
            }

            decodeMode = mode;
        }
    }

    public VideoPreviewSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return GetSnapshotUnderLock();
            }
        }
    }

    public VideoPreviewMedia? MediaInfo
    {
        get
        {
            lock (gate)
            {
                return opening ? null : current?.Media;
            }
        }
    }

    internal void InvalidatePreview()
    {
        lock (gate)
        {
            if (!closed)
            {
                revision++;
                pendingSeek = null;
                current?.ConversionCancellation?.Cancel();
            }
        }
    }

    internal Task RefreshPausedPreviewAsync()
    {
        lock (gate)
        {
            if (closed || opening || current is not { Error: null, Session: { } session } ||
                session.Snapshot.State != VideoPlaybackState.PAUSED)
            {
                return Task.CompletedTask;
            }

            return SeekAsync(session.Snapshot.Position);
        }
    }

    /// <summary>
    /// 立即使旧文件输出失效，等待旧资源回收，再探测并打开新文件。
    /// </summary>
    public async Task OpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        VideoDecodeMode mode;
        lock (gate)
        {
            mode = decodeMode;
        }

        await OpenCoreAsync(filePath, mode, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<VideoPreviewRun> OpenCoreAsync(string filePath, VideoDecodeMode mode,
        MediaTime? initialPosition, CancellationToken cancellationToken, VideoPreviewOpenRequest? request = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(filePath);
        long requestedEpoch;
        VideoPreviewRun? previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (request is not null)
            {
                ThrowIfObsoleteUnderLock(request.ExpectedEpoch);
            }
            CancelPlaybackRangeUnderLock();
            requestedEpoch = ++epoch;
            mediaRangeInstalled = false;
            if (request is not null)
            {
                request.AllocatedEpoch = requestedEpoch;
            }
            revision++;
            pendingSeek = null;
            requestedPath = path;
            opening = true;
            previous = current;
            ClearPresentationMeasurementUnderLock(previous);
            BeginOperationUnderLock();
        }

        VideoPreviewRun? run = null;
        var acquired = false;
        try
        {
            _ = previous?.Stop();
            await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            acquired = true;
            await RetireAsync(previous).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                ThrowIfObsoleteUnderLock(requestedEpoch);
                run = new(requestedEpoch, path);
                current = run;
            }

            using var registration = cancellationToken.UnsafeRegister(static value => { _ = ((VideoPreviewRun)value!).Stop(); }, run);
            await DispatchAsync(run, null, null, true, run.Token).ConfigureAwait(false);
            var media = await probe(path, run.Token).ConfigureAwait(false);
            run.Token.ThrowIfCancellationRequested();
            if (audioFactory is not null && media.AudioStreamIndex is { } audioIndex)
            {
                try
                {
                    var audio = await audioFactory(path, audioIndex, media.Start ?? MediaTime.Zero, run.Token).ConfigureAwait(false);
                    try
                    {
                        lock (gate)
                        {
                            audio.SetGain(muted ? 0 : volume);
                        }

                        run.AttachAudio(audio);
                    }
                    catch
                    {
                        await audio.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }
                catch (Exception audioError) when (audioError is not OperationCanceledException)
                {
                    run.AudioError = audioError;
                }
            }

            Func<MediaTime?>? audioClock = run.Audio is { } attachedAudio
                ? () => run.AudioError is null && attachedAudio.Error is null ? attachedAudio.Position : null
                : null;
            var session = sessionFactory(path, media.VideoStreamIndex, audioClock, new() { Mode = mode });
            try
            {
                run.Attach(session);
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            lock (gate)
            {
                run.Media = media;
            }

            await session.OpenAsync(run.Token).ConfigureAwait(false);
            var decodedStart = session.Snapshot.DisplayTime;
            if (initialPosition is { } target)
            {
                await session.SeekAsync(target, run.Token).ConfigureAwait(false);
            }
            if (run.Audio is { } openedAudio)
            {
                await TryAudioAsync(run, () => openedAudio.SeekAsync(session.Snapshot.Position)).ConfigureAwait(false);
            }
            lock (gate)
            {
                ThrowIfObsoleteUnderLock(requestedEpoch);
                opening = false;
                run.Media = media with { Start = media.Start ?? decodedStart };
                run.Pump = Task.Run(() => PumpAsync(run, session), CancellationToken.None);
            }

            await DispatchAsync(run, null, null, false, run.Token).ConfigureAwait(false);
            return run;
        }
        catch (Exception error)
        {
            if (run is not null)
            {
                bool report;
                lock (gate)
                {
                    report = IsCurrentUnderLock(run) && !run.Token.IsCancellationRequested && error is not OperationCanceledException;
                    if (report)
                    {
                        run.Error = error;
                    }

                    if (IsCurrentUnderLock(run))
                    {
                        opening = false;
                    }
                }

                try
                {
                    if (report)
                    {
                        await DispatchAsync(run, null, null, true, run.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
                {
                }
                finally
                {
                    await RetireAsync(run).ConfigureAwait(false);
                }
            }

            lock (gate)
            {
                ThrowIfObsoleteUnderLock(requestedEpoch);
            }

            throw;
        }
        finally
        {
            if (acquired)
            {
                operationGate.Release();
            }
            lock (gate)
            {
                if (epoch == requestedEpoch)
                {
                    opening = false;
                }

                CompleteOperationUnderLock();
            }
        }
    }

    /// <summary>
    /// 在当前媒体位置重建解码会话，确认转换后的首帧交付后恢复播放；失败时恢复原模式和播放状态。
    /// 尚未打开媒体时仅更新下一次打开所用的模式。
    /// </summary>
    public async Task SwitchDecodeModeAsync(VideoDecodeMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        cancellationToken.ThrowIfCancellationRequested();
        VideoPreviewSnapshot previous;
        VideoDecodeMode previousMode;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (switchingDecodeMode)
            {
                throw new InvalidOperationException("视频解码类型正在切换。");
            }
            if (mode == decodeMode)
            {
                return;
            }
            if (opening)
            {
                throw new InvalidOperationException("请等待视频打开完成后再切换解码类型。");
            }

            previousMode = decodeMode;
            previous = GetSnapshotUnderLock();
            if (previous.FilePath is null)
            {
                decodeMode = mode;
                return;
            }

            switchingDecodeMode = true;
            BeginOperationUnderLock();
        }

        var request = new VideoPreviewOpenRequest(previous.Epoch);
        try
        {
            var run = await OpenCoreAsync(previous.FilePath, mode, previous.Position, cancellationToken, request).ConfigureAwait(false);
            await WaitForFirstPresentationAsync(run, cancellationToken).ConfigureAwait(false);
            if (previous.State == VideoPlaybackState.PLAYING)
            {
                await ResumeRunAsync(run).ConfigureAwait(false);
            }

            lock (gate)
            {
                ThrowIfObsoleteUnderLock(run.Epoch);
                decodeMode = mode;
            }
        }
        catch (Exception switchError)
        {
            bool restore;
            lock (gate)
            {
                restore = !closed && request.AllocatedEpoch is { } attemptedEpoch && epoch == attemptedEpoch;
            }
            if (restore)
            {
                var restoration = new VideoPreviewOpenRequest(request.AllocatedEpoch!.Value);
                try
                {
                    var restored = await OpenCoreAsync(previous.FilePath, previousMode, previous.Position,
                        CancellationToken.None, restoration).ConfigureAwait(false);
                    await WaitForFirstPresentationAsync(restored, CancellationToken.None).ConfigureAwait(false);
                    if (previous.State == VideoPlaybackState.PLAYING)
                    {
                        await ResumeRunAsync(restored).ConfigureAwait(false);
                    }
                }
                catch (Exception) when (IsOpenRequestObsolete(restoration))
                {
                }
                catch (Exception restoreError)
                {
                    throw new AggregateException("切换视频解码类型失败，恢复原预览时也发生错误。", switchError, restoreError);
                }
            }

            throw;
        }
        finally
        {
            lock (gate)
            {
                switchingDecodeMode = false;
                CompleteOperationUnderLock();
            }
        }
    }

    private bool IsOpenRequestObsolete(VideoPreviewOpenRequest request)
    {
        lock (gate)
        {
            return closed || epoch != (request.AllocatedEpoch ?? request.ExpectedEpoch);
        }
    }

    private static Task<bool> WaitForFirstPresentationAsync(VideoPreviewRun run, CancellationToken cancellationToken)
    {
        if (run.Session?.Snapshot.DisplayTime is null)
        {
            throw new InvalidDataException("视频没有可转换的首帧。");
        }

        return run.FirstPresentation.Task.WaitAsync(cancellationToken);
    }

    private Task ResumeRunAsync(VideoPreviewRun run)
    {
        lock (gate)
        {
            ThrowIfObsoleteUnderLock(run.Epoch);
            return PlayAsync();
        }
    }

    /// <summary>
    /// 取消当前打开并回收影音资源，清空画面；控制器保持可再次打开媒体。
    /// </summary>
    public async Task CloseMediaAsync()
    {
        VideoPreviewRun? previous;
        long requestedEpoch;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            CancelPlaybackRangeUnderLock();
            requestedEpoch = ++epoch;
            mediaRangeInstalled = false;
            revision++;
            pendingSeek = null;
            opening = false;
            requestedPath = null;
            previous = current;
            current = null;
            ClearPresentationMeasurementUnderLock(previous);
            BeginOperationUnderLock();
        }

        try
        {
            _ = previous?.Stop();
            await operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await RetireAsync(previous).ConfigureAwait(false);
            }
            finally
            {
                operationGate.Release();
            }

            await dispatchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await dispatch(() =>
                {
                    lock (gate)
                    {
                        if (!closed && epoch == requestedEpoch && current is null)
                        {
                            present(new(GetSnapshotUnderLock(), null, true));
                        }
                    }
                }, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                dispatchGate.Release();
            }
        }
        finally
        {
            lock (gate)
            {
                CompleteOperationUnderLock();
            }
        }
    }

    /// <summary>
    /// 开始原速影音播放，存在可用音轨时以音频设备消费时钟同步视频。
    /// </summary>
    public Task PlayAsync()
    {
        lock (gate)
        {
            CancelPlaybackRangeUnderLock();
        }
        return ExecuteAsync(async (run, session, operationRevision) =>
        {
            await ClearMediaRangeAsync(run, session, operationRevision).ConfigureAwait(false);
            await SubmitAudioCommandAsync(run, operationRevision, audio => audio.PlayAsync()).ConfigureAwait(false);

            Task playback;
            lock (gate)
            {
                ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                playback = session.PlayAsync();
            }

            await playback.ConfigureAwait(false);
        }, false);
    }

    /// <summary>
    /// 立即使旧转换结果失效，并定位到暂停帧。
    /// </summary>
    public Task PauseAsync()
    {
        lock (gate)
        {
            CancelPlaybackRangeUnderLock();
        }
        return ExecuteAsync(async (run, session, operationRevision) =>
        {
            Task pause;
            lock (gate)
            {
                ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                pause = Task.WhenAll(SubmitAudioCommandAsync(run, operationRevision, audio => audio.PauseAsync()), session.PauseAsync());
            }
            await pause.ConfigureAwait(false);
        }, true);
    }

    /// <summary>
    /// 立即使旧转换结果失效，按原始媒体时间定位，完成后保持暂停。
    /// 相同目标的在途请求共享一次定位；完成后的请求仍重新渲染工程样式。
    /// </summary>
    public Task SeekAsync(MediaTime target)
    {
        lock (gate)
        {
            CancelPlaybackRangeUnderLock();
            if (!closed && pendingSeek is { IsCompleted: false } && pendingSeekTarget == target &&
                pendingSeekEpoch == epoch && pendingSeekSequence == commandSequence)
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
                Task audioSeek;
                lock (gate)
                {
                    ThrowIfCommandObsoleteUnderLock(run, operationRevision);
                    audioSeek = run.AudioError is null && run.Audio is { Error: null } readyAudio
                        ? TryAudioAsync(run, () => readyAudio.SeekAsync(session.Snapshot.Position))
                        : Task.CompletedTask;
                }

                await audioSeek.ConfigureAwait(false);
            }, true);
            pendingSeekTarget = target;
            pendingSeekEpoch = epoch;
            pendingSeekSequence = commandSequence;
            return pendingSeek;
        }
    }

    /// <summary>设置预览音量，范围为零到一。</summary>
    public void SetVolume(float value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        lock (gate)
        {
            volume = value;
            current?.Audio?.SetGain(muted ? 0 : volume);
        }
    }

    /// <summary>切换静音，保留用户音量并持续推进音频时钟。</summary>
    public void SetMuted(bool value)
    {
        lock (gate)
        {
            muted = value;
            current?.Audio?.SetGain(muted ? 0 : volume);
        }
    }

    /// <summary>
    /// 终止打开、转换和播放，等待回收后清空 UI；重复调用等待同一次关闭。
    /// </summary>
    public Task CloseAsync()
    {
        lock (gate)
        {
            if (closeTask is not null)
            {
                return closeTask;
            }
            CancelPlaybackRangeUnderLock();
            closed = true;
            mediaRangeInstalled = false;
            opening = false;
            epoch++;
            revision++;
            pendingSeek = null;
            var run = current;
            ClearPresentationMeasurementUnderLock(run);
            _ = run?.Stop();
            closeTask = CloseCoreAsync(run);
            return closeTask;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            operationGate.Dispose();
            dispatchGate.Dispose();
        }
    }

    private async Task ExecuteAsync(Func<VideoPreviewRun, VideoPlaybackSession, long, Task> command, bool invalidate)
    {
        VideoPreviewRun run;
        Task result;
        long operationRevision;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            run = current ?? throw new InvalidOperationException("视频尚未打开。");
            if (opening || run.Error is not null || run.Session is not { } session || run.Token.IsCancellationRequested)
            {
                throw new InvalidOperationException("视频尚未就绪。");
            }

            if (invalidate)
            {
                revision++;
                run.ConversionCancellation?.Cancel();
                ClearPresentationMeasurementUnderLock(run);
            }

            operationRevision = revision;
            commandSequence++;
            result = command(run, session, operationRevision);
            BeginOperationUnderLock();
            PulseResumeUnderLock(run);
        }

        try
        {
            await result.ConfigureAwait(false);
            await DispatchAsync(run, null, null, false, run.Token, operationRevision).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                PulseResumeUnderLock(run);
                CompleteOperationUnderLock();
            }
        }
    }

    private async Task PumpAsync(VideoPreviewRun run, VideoPlaybackSession session)
    {
        try
        {
            using var converter = converterFactory();
            while (!run.Token.IsCancellationRequested)
            {
                Task resume;
                lock (gate)
                {
                    resume = run.Resume.Task;
                }

                using var presentation = await session.ReadPresentationAsync(run.Token).ConfigureAwait(false);
                if (presentation is null)
                {
                    var pause = Task.CompletedTask;
                    lock (gate)
                    {
                        if (rangeCancellation is null && IsCurrentUnderLock(run) && session.Snapshot.State == VideoPlaybackState.ENDED &&
                            run.AudioError is null && run.Audio is { Error: null } audio)
                        {
                            pause = TryAudioAsync(run, audio.PauseAsync);
                        }
                    }
                    await pause.ConfigureAwait(false);
                    await resume.WaitAsync(run.Token).ConfigureAwait(false);
                    continue;
                }

                VideoPreviewDelivery identity;
                lock (gate)
                {
                    identity = new(session, presentation.Generation, revision,
                        presentation.PositionedFrame.Time, presentation.PositionedFrame.NextFrameTime);
                    if (!IsPresentationCurrentUnderLock(run, identity))
                    {
                        continue;
                    }
                }

                using var conversion = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
                lock (gate)
                {
                    if (!IsPresentationCurrentUnderLock(run, identity))
                    {
                        continue;
                    }
                    run.ConversionCancellation = conversion;
                }
                try
                {
                    var frame = converter.Convert(presentation.PositionedFrame.Frame, conversion.Token);
                    await DispatchAsync(run, identity, frame, false, run.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (conversion.IsCancellationRequested && !run.Token.IsCancellationRequested)
                {
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(run.ConversionCancellation, conversion))
                        {
                            run.ConversionCancellation = null;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            lock (gate)
            {
                if (!IsCurrentUnderLock(run))
                {
                    return;
                }

                run.Error = error;
                run.FirstPresentation.TrySetException(error);
            }

            try
            {
                await DispatchAsync(run, null, null, false, run.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
            {
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchAsync(VideoPreviewRun run,
        VideoPreviewDelivery? identity,
        SdrVideoFrame? frame, bool clear, CancellationToken cancellationToken, long? commandRevision = null)
    {
        lock (gate)
        {
            if (!IsCurrentUnderLock(run) || commandRevision is { } beforeQueue && beforeQueue != revision)
            {
                return;
            }
        }

        await dispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (!CanPresentUnderLock(run, identity) || commandRevision is { } beforeDispatch && beforeDispatch != revision)
                {
                    return;
                }
            }

            await dispatch(() =>
            {
                lock (gate)
                {
                    if (!CanPresentUnderLock(run, identity) || commandRevision is { } beforePresent && beforePresent != revision)
                    {
                        return;
                    }

                    var snapshot = GetSnapshotUnderLock();
                    if (identity is { } delivery && frame is not null)
                    {
                        snapshot = snapshot with
                        {
                            PresentedFrameTime = delivery.Time,
                            PresentedAtPosition = snapshot.Position,
                            PresentedGeneration = delivery.Generation
                        };
                    }

                    present(new(snapshot, frame, clear));
                    if (identity is { } completed && frame is not null && IsDeliveryIdentityCurrentUnderLock(run, completed))
                    {
                        run.PresentedFrameTime = snapshot.PresentedFrameTime;
                        run.PresentedAtPosition = snapshot.PresentedAtPosition;
                        run.PresentedGeneration = snapshot.PresentedGeneration;
                        run.FirstPresentation.TrySetResult(true);
                    }
                    else if (clear && IsCurrentUnderLock(run))
                    {
                        ClearPresentationMeasurementUnderLock(run);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            dispatchGate.Release();
        }
    }

    private bool CanPresentUnderLock(VideoPreviewRun run, VideoPreviewDelivery? identity)
    {
        return IsCurrentUnderLock(run) && (identity is not { } value ||
            IsPresentationCurrentUnderLock(run, value));
    }

    private async Task TryAudioAsync(VideoPreviewRun run, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lock (gate)
            {
                run.AudioError = exception;
            }
        }
    }

    private bool IsPresentationCurrentUnderLock(VideoPreviewRun run, VideoPreviewDelivery delivery)
    {
        var playback = delivery.Session.Snapshot;
        return IsDeliveryIdentityCurrentUnderLock(run, delivery) &&
            (run.PresentedGeneration != delivery.Generation || run.PresentedFrameTime is not { } previous || delivery.Time >= previous) &&
            (playback.State != VideoPlaybackState.ENDED || playback.DisplayTime == delivery.Time);
    }

    private bool IsDeliveryIdentityCurrentUnderLock(VideoPreviewRun run, VideoPreviewDelivery delivery)
    {
        return IsCurrentUnderLock(run) && ReferenceEquals(run.Session, delivery.Session) &&
            delivery.Session.Snapshot.Generation == delivery.Generation && revision == delivery.Revision;
    }

    private static void ClearPresentationMeasurementUnderLock(VideoPreviewRun? run)
    {
        if (run is null)
        {
            return;
        }

        run.PresentedFrameTime = null;
        run.PresentedAtPosition = null;
        run.PresentedGeneration = null;
    }

    private bool IsCurrentUnderLock(VideoPreviewRun run)
    {
        return !closed && ReferenceEquals(current, run) && epoch == run.Epoch;
    }

    private void ThrowIfCommandObsoleteUnderLock(VideoPreviewRun run, long operationRevision)
    {
        if (!IsCurrentUnderLock(run) || revision != operationRevision)
        {
            throw new OperationCanceledException("影音控制请求已被替换或关闭。");
        }
    }

    private void ThrowIfObsoleteUnderLock(long requestedEpoch)
    {
        if (closed || epoch != requestedEpoch)
        {
            throw new OperationCanceledException("预览请求已被替换或关闭。");
        }
    }

    private VideoPreviewSnapshot GetSnapshotUnderLock()
    {
        var playback = current?.Session?.Snapshot;
        var state = closed ? VideoPlaybackState.CLOSED : opening ? VideoPlaybackState.OPENING :
            current?.Error is not null ? VideoPlaybackState.FAULTED : playback?.State ?? VideoPlaybackState.CREATED;
        return new(requestedPath, state, opening, playback?.Position ?? MediaTime.Zero,
            opening ? null : current?.Media?.Start, opening ? null : current?.Media?.Duration,
            opening ? null : current?.Error ?? playback?.Error, epoch,
            current?.PresentedFrameTime, current?.PresentedAtPosition, current?.PresentedGeneration,
            current?.Audio is not null, current?.AudioError ?? current?.Audio?.Error, volume, muted,
            current?.Session?.DecodeSessionInfo);
    }

    private void BeginOperationUnderLock()
    {
        if (activeOperations++ == 0)
        {
            operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void CompleteOperationUnderLock()
    {
        if (--activeOperations == 0)
        {
            operationsDrained!.TrySetResult(true);
        }
    }

    private static void PulseResumeUnderLock(VideoPreviewRun run)
    {
        var previous = run.Resume;
        run.Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult(true);
    }

    private static async Task RetireAsync(VideoPreviewRun? run)
    {
        if (run is null)
        {
            return;
        }

        try
        {
            await run.Stop().ConfigureAwait(false);
            await run.Pump.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Task.WhenAll(run.Session?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    run.Audio?.DisposeAsync().AsTask() ?? Task.CompletedTask).ConfigureAwait(false);
            }
            finally
            {
                run.Dispose();
            }
        }
    }

    private async Task CloseCoreAsync(VideoPreviewRun? run)
    {
        Task pending;
        Task playbackRange;
        Task playbackStop;
        lock (gate)
        {
            pending = operationsDrained?.Task ?? Task.CompletedTask;
            playbackRange = rangeWorker;
            playbackStop = rangeStop;
        }

        await pending.ConfigureAwait(false);
        await Task.WhenAll(playbackRange, playbackStop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RetireAsync(run).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }

        await dispatchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await dispatch(() => present(new(Snapshot, null, true)), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            dispatchGate.Release();
        }
    }
}
