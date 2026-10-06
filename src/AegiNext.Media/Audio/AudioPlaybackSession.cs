using AegiNext.Core.Timing;
using System.Diagnostics;

namespace AegiNext.Media.Audio;

/// <summary>
/// 使用系统输出时钟与设备校准驱动音频主时钟；设备 PCM 队列最多 250ms。
/// </summary>
public sealed class AudioPlaybackSession : IAsyncDisposable
{
    private const int SAMPLE_RATE = 48000;
    private const int TARGET_FRAMES = 9600;
    private readonly IAudioSampleSource source;
    private readonly IAudioOutput output;
    private Func<AudioOutputClockSnapshot, MediaTime>? calibration;
    private readonly Lock stateGate = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private readonly float[] silence = new float[8192];
    private AudioSampleBlock? pending;
    private int pendingOffset;
    private long originSample;
    private long submitted;
    private long nextSample;
    private MediaTime position;
    private bool playing;
    private bool closed;
    private bool eof;
    private Exception? error;
    private Task? closeTask;
    private Task commands = Task.CompletedTask;
    private long controlRevision;
    private MediaTimeRange? playbackRange;
    private bool reachedPlaybackRangeEnd;
    private long? rangeDrainedAt;
    private AudioOutputClockSnapshot clockSnapshot = new(0, 0, 1, "unknown", "estimated", 0, AudioClockQuality.ESTIMATED, 0);
    private long clockEpoch;
    private string clockDeviceId = "unknown";

    /// <summary>
    /// 接管已创建的源和输出；调用方在后台线程构造，初始设备暂停并定位到原始时间轴目标。
    /// </summary>
    public AudioPlaybackSession(IAudioSampleSource source, IAudioOutput output, MediaTime initialPosition,
        Func<AudioOutputClockSnapshot, MediaTime>? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        if (source.Format.SampleRate != SAMPLE_RATE || source.Format.Channels != 2)
        {
            throw new ArgumentException("播放源必须是 48kHz 立体声。", nameof(source));
        }

        this.source = source;
        this.output = output;
        this.calibration = calibration;
        source.Seek(initialPosition);
        output.SetPaused(true);
        output.Clear();
        ResetPosition(initialPosition);
        worker = Task.Run(RunAsync);
    }

    public MediaTime Position
    {
        get
        {
            lock (stateGate)
            {
                try
                {
                    return ReadPositionUnderLock();
                }
                catch (Exception exception)
                {
                    RecordFailureUnderLock(exception);
                    return position;
                }
            }
        }
    }

    public Exception? Error
    {
        get
        {
            lock (stateGate)
            {
                return error;
            }
        }
    }

    public AudioOutputClockSnapshot ClockSnapshot
    {
        get
        {
            lock (stateGate)
            {
                _ = Position;
                return error is null ? clockSnapshot : clockSnapshot with { Quality = AudioClockQuality.UNAVAILABLE };
            }
        }
    }

    /// <summary>在暂停时替换按设备读取的校准；调用方随后以原位置 seek 重建输出时间映射。</summary>
    public void ConfigureCalibration(Func<AudioOutputClockSnapshot, MediaTime>? provider)
    {
        lock (stateGate)
        {
            ThrowIfUnavailable();
            if (playing)
            {
                throw new InvalidOperationException("更改音频设备校准前必须暂停播放。");
            }
            calibration = provider;
        }
    }

    public int BufferedFrames
    {
        get
        {
            lock (stateGate)
            {
                return closed ? 0 : output.QueuedFrames;
            }
        }
    }

    public bool ReachedPlaybackRangeEnd
    {
        get
        {
            lock (stateGate)
            {
                return reachedPlaybackRangeEnd;
            }
        }
    }

    /// <summary>暂停并重新定位播放范围，清除旧缓冲；null 恢复无范围播放但保持暂停。</summary>
    public Task SetPlaybackRangeAsync(MediaTimeRange? range)
    {
        lock (stateGate)
        {
            ThrowIfUnavailable();
            controlRevision++;
            output.SetPaused(true);
            var target = ReadPositionUnderLock();
            playing = false;
            return EnqueueUnderLock(async () =>
            {
                await operationGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    lock (stateGate)
                    {
                        playbackRange = range;
                    }
                    ResetDecoder(range?.Start ?? target);
                }
                finally
                {
                    operationGate.Release();
                }
            });
        }
    }

    /// <summary>在后台打开默认输出设备与指定音轨，初始暂停。</summary>
    public static Task<AudioPlaybackSession> OpenAsync(string path, int streamIndex, MediaTime initialPosition,
        CancellationToken cancellationToken = default)
    {
        return OpenAsync(path, streamIndex, initialPosition, null, cancellationToken);
    }

    /// <summary>打开系统输出设备，并按设备身份读取额外出声延迟校准。</summary>
    public static Task<AudioPlaybackSession> OpenAsync(string path, int streamIndex, MediaTime initialPosition,
        Func<AudioOutputClockSnapshot, MediaTime>? calibration, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var source = FfmpegAudioDecoder.Open(path, streamIndex, cancellationToken: cancellationToken);
            SystemAudioOutput? output = null;
            try
            {
                using var registration = cancellationToken.UnsafeRegister(static state => ((IAudioSampleSource)state!).Cancel(), source);
                output = new SystemAudioOutput();
                cancellationToken.ThrowIfCancellationRequested();
                return new AudioPlaybackSession(source, output, initialPosition, calibration);
            }
            catch
            {
                output?.Dispose();
                source.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>填充有限预缓冲后恢复设备消费。</summary>
    public Task PlayAsync()
    {
        lock (stateGate)
        {
            ThrowIfUnavailable();
            var requestedRevision = ++controlRevision;
            return EnqueueUnderLock(() => PlayCoreAsync(requestedRevision));
        }
    }

    private async Task PlayCoreAsync(long requestedRevision)
    {
        await operationGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            Fill();
            lock (stateGate)
            {
                ThrowIfUnavailable();
                if (requestedRevision != controlRevision)
                {
                    throw new OperationCanceledException("音频恢复请求已被暂停或新的操作替换。");
                }

                output.SetPaused(false);
                playing = true;
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    /// <summary>立即暂停设备并冻结消费时钟，保留尚未消费的样本。</summary>
    public Task PauseAsync()
    {
        lock (stateGate)
        {
            if (closed)
            {
                return Task.CompletedTask;
            }

            controlRevision++;
            output.SetPaused(true);
            _ = ReadPositionUnderLock();
            playing = false;
            rangeDrainedAt = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>暂停并清空旧设备数据，回退解码并按样本精度裁切目标前内容。</summary>
    public Task SeekAsync(MediaTime target)
    {
        lock (stateGate)
        {
            ThrowIfUnavailable();
            controlRevision++;
            output.SetPaused(true);
            _ = ReadPositionUnderLock();
            playing = false;
            return EnqueueUnderLock(() => SeekCoreAsync(target));
        }
    }

    private async Task SeekCoreAsync(MediaTime target)
    {
        await operationGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            ResetDecoder(target);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private void ResetDecoder(MediaTime target)
    {
        source.Seek(target, lifetime.Token);
        lock (stateGate)
        {
            ThrowIfUnavailable();
            output.Clear();
            ResetPosition(target);
            reachedPlaybackRangeEnd = false;
            rangeDrainedAt = null;
        }
        pending = null;
        pendingOffset = 0;
        eof = false;
        Fill();
    }

    /// <summary>设置音量；静音可通过零增益实现，不改变播放时钟。</summary>
    public void SetGain(float gain)
    {
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            output.SetGain(gain);
        }
    }

    /// <summary>取消阻塞读取、暂停输出并等待工作循环和操作排空。</summary>
    public Task CloseAsync()
    {
        lock (stateGate)
        {
            if (closeTask is not null)
            {
                return closeTask;
            }

            closed = true;
            playing = false;
            controlRevision++;
            closeTask = Task.Run(() => CloseCoreAsync(commands));
            return closeTask;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await operationGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    Fill();
                }
                finally
                {
                    operationGate.Release();
                }

                await Task.Delay(5, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (stateGate)
            {
                RecordFailureUnderLock(exception);
            }
        }
    }

    private void Fill()
    {
        while (!lifetime.IsCancellationRequested)
        {
            var capacity = Math.Min(4096, TARGET_FRAMES - output.QueuedFrames);
            if (playbackRange is { } range)
            {
                var endSample = range.End.ToTimestamp(new(1, SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
                capacity = (int)Math.Min(capacity, Math.Max(0, endSample - nextSample));
                if (nextSample >= endSample && output.QueuedFrames == 0)
                {
                    lock (stateGate)
                    {
                        if (playing)
                        {
                            rangeDrainedAt ??= Stopwatch.GetTimestamp();
                            var clock = output.ReadClock();
                            var tail = clock.Quality == AudioClockQuality.SYSTEM
                                ? Math.Max(0, (calibration?.Invoke(clock) ?? MediaTime.Zero).ToTimeSpan(MediaTimeRounding.CEILING).TotalSeconds)
                                : (double)output.LatencyFrames / SAMPLE_RATE;
                            if (Stopwatch.GetElapsedTime(rangeDrainedAt.Value).TotalSeconds >= tail)
                            {
                                position = range.End;
                                playing = false;
                                reachedPlaybackRangeEnd = true;
                                output.SetPaused(true);
                            }
                        }
                    }
                }
            }
            if (capacity <= 0)
            {
                return;
            }

            if (pending is null && !eof)
            {
                pending = source.Read(lifetime.Token);
                pendingOffset = 0;
                eof = pending is null;
            }

            var blockSample = pending?.Start.ToTimestamp(new(1, SAMPLE_RATE), MediaTimeRounding.CEILING).Value ?? nextSample;
            if (pending is not null && blockSample + pendingOffset < nextSample)
            {
                pendingOffset += (int)Math.Min(pending.FrameCount - pendingOffset, nextSample - blockSample - pendingOffset);
                if (pendingOffset == pending.FrameCount)
                {
                    pending = null;
                    continue;
                }
            }

            int frames;
            if (pending is null || blockSample + pendingOffset > nextSample)
            {
                frames = pending is null ? capacity : (int)Math.Min(capacity, blockSample + pendingOffset - nextSample);
                output.Write(silence.AsSpan(0, frames * 2));
            }
            else
            {
                frames = Math.Min(capacity, pending.FrameCount - pendingOffset);
                output.Write(pending.Samples.Span.Slice(pendingOffset * 2, frames * 2));
                pendingOffset += frames;
                if (pendingOffset == pending.FrameCount)
                {
                    pending = null;
                }
            }

            nextSample += frames;
            lock (stateGate)
            {
                submitted += frames;
            }
        }
    }

    private MediaTime ReadPositionUnderLock()
    {
        if (playing && !closed && error is null)
        {
            clockSnapshot = output.ReadClock();
            if (clockSnapshot.Quality == AudioClockQuality.UNAVAILABLE || clockSnapshot.Epoch != clockEpoch ||
                clockSnapshot.DeviceId != clockDeviceId || clockSnapshot.PlayedFrames < 0 || clockSnapshot.HostFrequency <= 0)
            {
                throw new IOException("音频输出时钟已失效或设备发生变化，需要重新打开播放设备。");
            }

            var consumed = clockSnapshot.Quality == AudioClockQuality.SYSTEM
                ? Math.Clamp(clockSnapshot.PlayedFrames, 0, submitted)
                : Math.Max(0, submitted - clockSnapshot.QueuedFrames - output.LatencyFrames);
            if (consumed == 0)
            {
                return position;
            }
            var estimated = new MediaTime(checked(originSample + consumed), SAMPLE_RATE);
            estimated -= calibration?.Invoke(clockSnapshot) ?? MediaTime.Zero;
            if (estimated > position)
            {
                position = playbackRange is { } range && estimated > range.End ? range.End : estimated;
            }
        }

        return position;
    }

    private void ResetPosition(MediaTime target)
    {
        originSample = target.ToTimestamp(new(1, SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        nextSample = originSample;
        submitted = 0;
        position = target;
        clockSnapshot = output.ReadClock();
        if (clockSnapshot.Quality == AudioClockQuality.UNAVAILABLE)
        {
            throw new IOException("音频输出设备没有可用的播放时钟。");
        }
        clockEpoch = clockSnapshot.Epoch;
        clockDeviceId = clockSnapshot.DeviceId;
    }

    private void ThrowIfUnavailable()
    {
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (error is not null)
            {
                throw new InvalidOperationException("音频播放失败。", error);
            }
        }
    }

    private Task EnqueueUnderLock(Func<Task> operation)
    {
        var previous = commands;
        commands = Task.Run(async () =>
        {
            await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not ObjectDisposedException)
            {
                lock (stateGate)
                {
                    RecordFailureUnderLock(exception);
                }

                throw;
            }
        });
        return commands;
    }

    private void RecordFailureUnderLock(Exception exception)
    {
        error = exception;
        playing = false;
        try
        {
            output.SetPaused(true);
        }
        catch (Exception pauseError)
        {
            error = new AggregateException(exception, pauseError);
        }
    }

    private async Task CloseCoreAsync(Task pendingCommands)
    {
        var failures = new List<Exception>();
        try
        {
            lifetime.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            source.Cancel();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            output.SetPaused(true);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        await pendingCommands.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await worker.ConfigureAwait(false);
        try
        {
            source.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            output.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        pending = null;
        operationGate.Dispose();
        lifetime.Dispose();
        if (failures.Count > 0)
        {
            throw new AggregateException("关闭音频时发生错误；所有资源均已尝试回收。", failures);
        }
    }
}
