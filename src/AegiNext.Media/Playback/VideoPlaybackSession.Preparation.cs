using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Playback;

public sealed partial class VideoPlaybackSession
{
    private readonly Queue<PreparedVideoTiming> preparedTimings = new();
    private VideoPreparationOptions? preparationOptions;
    private MediaTime preparationLead;
    private int preparationPendingCount;
    private long preparationPendingBytes;

    /// <summary>取得会话用于调度和处理耗时测量的同一个单调时间提供者。</summary>
    public TimeProvider TimeProvider => timeProvider;

    /// <summary>在打开前选择提前准备模式；原始帧和解码游标仍由本会话独占。</summary>
    public void ConfigurePreparation(VideoPreparationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closeRequested, this);
            if (snapshot.State != VideoPlaybackState.CREATED)
            {
                throw new InvalidOperationException("只能在打开视频前配置提前准备。");
            }
            preparationOptions = options;
        }
    }

    /// <summary>根据已测得的转换和交付成本调整准备目标，始终限制在配置的提前范围内。</summary>
    public void SetPreparationLead(MediaTime lead)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lead, MediaTime.Zero);
        lock (gate)
        {
            var options = preparationOptions ?? throw new InvalidOperationException("会话没有启用提前准备。");
            preparationLead = lead < options.MaximumAhead ? lead : options.MaximumAhead;
            PulseCommandUnderLock();
        }
    }

    /// <summary>接管一帧用于提前转换；帧可能尚未到显示时间，调用方必须根据真实区间调度呈现。</summary>
    public ValueTask<VideoPresentation?> ReadPreparationAsync(CancellationToken cancellationToken = default)
    {
        return ReadPresentationCoreAsync(true, cancellationToken);
    }

    /// <summary>等待原始媒体时钟到达目标；暂停、结束或请求变更会返回以便调用方重新验证身份。</summary>
    public Task WaitForPositionAsync(MediaTime target, CancellationToken cancellationToken = default)
    {
        return WaitForPositionAsync(target, Snapshot.Generation, cancellationToken);
    }

    /// <summary>只在指定代次中等待目标；新定位或暂停会立即终止旧帧的等待。</summary>
    public async Task WaitForPositionAsync(MediaTime target, long generation, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task changed;
            MediaTime remaining;
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                remaining = target - GetPositionUnderLock();
                if (closeRequested || snapshot.Generation != generation || snapshot.State != VideoPlaybackState.PLAYING || remaining <= MediaTime.Zero)
                {
                    return;
                }
                changed = commandChanged.Task;
            }
            using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            var delay = Task.Delay(remaining.ToTimeSpan(MediaTimeRounding.CEILING), timeProvider, delayCancellation.Token);
            await Task.WhenAny(changed, delay).WaitAsync(cancellationToken).ConfigureAwait(false);
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private MediaTime? GetPreparationDueUnderLock(out bool advance)
    {
        advance = false;
        UpdatePreparedTimingUnderLock();
        if (snapshot.State != VideoPlaybackState.PLAYING)
        {
            return null;
        }
        var position = GetPositionUnderLock();
        MediaTime? due = preparedTimings.TryPeek(out var timing) ? timing.Time - position : null;
        var hasRoom = preparationPendingCount < preparationOptions!.PendingCapacity;
        if (hasRoom && (needsResynchronization || nextFrameTime is not null))
        {
            var preparationDue = needsResynchronization ? MediaTime.Zero : nextFrameTime!.Value - position - preparationLead;
            if (preparationDue <= MediaTime.Zero)
            {
                advance = true;
                return MediaTime.Zero;
            }
            due = due is { } existing && existing < preparationDue ? existing : preparationDue;
        }
        if (playbackRange is { } range)
        {
            var boundary = range.End - position;
            due = due is { } existing && existing < boundary ? existing : boundary;
        }
        return due;
    }

    private void UpdatePreparedTimingUnderLock()
    {
        if (preparationOptions is null || snapshot.State != VideoPlaybackState.PLAYING)
        {
            return;
        }
        var position = GetPositionUnderLock();
        if (playbackRange is { } range && position >= range.End)
        {
            CompleteRangeUnderLock(range);
            return;
        }
        while (preparedTimings.TryPeek(out var timing) && timing.Time <= position)
        {
            preparedTimings.Dequeue();
            snapshot = snapshot with { DisplayTime = timing.Time };
            if (timing.ReachedEnd && playbackRange is null)
            {
                reachedEnd = true;
                snapshot = snapshot with { State = VideoPlaybackState.ENDED, Position = timing.Time };
                PulsePresentationUnderLock();
                return;
            }
        }
    }

    private void AdvancePreparation(long generation)
    {
        PositionedVideoFrame? frame = null;
        try
        {
            MediaTime target;
            lock (gate)
            {
                target = GetPositionUnderLock() + preparationLead;
            }
            frame = needsResynchronization
                ? source!.SeekFrame(target, () => IsSuperseded(generation), lifetime.Token)
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
                    var position = GetPositionUnderLock();
                    if (playbackRange is { } range && (position >= range.End || frame is not null && frame.Time >= range.End))
                    {
                        nextFrameTime = null;
                        needsResynchronization = false;
                        if (position >= range.End)
                        {
                            CompleteRangeUnderLock(range);
                        }
                        return;
                    }
                    if (frame is null)
                    {
                        nextFrameTime = null;
                        needsResynchronization = false;
                        return;
                    }
                    target = position + preparationLead;
                    if (playbackRange is { } bounded && target >= bounded.End)
                    {
                        target = position;
                    }
                    if (frame.NextFrameTime is null || frame.NextFrameTime > target)
                    {
                        nextFrameTime = frame.NextFrameTime;
                        needsResynchronization = false;
                        preparedTimings.Enqueue(new(frame.Time, frame.NextFrameTime, frame.ReachedEnd));
                        PublishUnderLock(frame, generation);
                        frame = null;
                        UpdatePreparedTimingUnderLock();
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

    private long GetPreparationFrameBytes(IVideoFrame frame)
    {
        var bytes = 0L;
        for (var index = 0; index < frame.Info.PlaneCount; index++)
        {
            var plane = frame.GetPlaneInfo(index);
            bytes = checked(bytes + Math.Max(plane.RowBytes, Math.Abs((long)plane.SourceStride)) * plane.Height);
        }
        var options = preparationOptions!;
        if (bytes > options.MaximumPendingBytes / options.PendingCapacity)
        {
            throw new NotSupportedException("视频原始帧超过提前准备的单帧内存预算。");
        }
        return bytes;
    }

    private void ReleasePreparation(long bytes)
    {
        lock (gate)
        {
            preparationPendingCount--;
            preparationPendingBytes -= bytes;
            PulseCommandUnderLock();
        }
    }
}
