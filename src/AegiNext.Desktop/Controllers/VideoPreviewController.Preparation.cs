using AegiNext.Core.Timing;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controllers;

public sealed partial class VideoPreviewController
{
    private const long MAXIMUM_PREPARED_BYTES = 128L * 1024 * 1024;
    private const int PREPARATION_OBSERVATION_COUNT = 16;

    private async Task PrepareFramesAsync(VideoPreviewRun run, VideoPlaybackSession session,
        IVideoPreviewConverter converter, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await run.PreparedSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            var queued = false;
            try
            {
                Task resume;
                lock (gate)
                {
                    resume = run.Resume.Task;
                }
                using var presentation = await session.ReadPreparationAsync(cancellationToken).ConfigureAwait(false);
                if (presentation is null)
                {
                    run.PreparedSlots.Release();
                    queued = true;
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
                    await resume.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                VideoPreviewDelivery identity;
                lock (gate)
                {
                    identity = new(session, presentation.Generation, revision,
                        presentation.PositionedFrame.Time, presentation.PositionedFrame.NextFrameTime,
                        run.PreparationCancellation.Token);
                    var playback = session.Snapshot;
                    if (!IsPresentationCurrentUnderLock(run, identity) || playback.State == VideoPlaybackState.PLAYING &&
                        identity.NextTime is { } next && next <= playback.Position + run.ConversionLead + run.DispatchLead)
                    {
                        continue;
                    }
                }

                using var conversion = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, identity.PreparationToken);
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
                    var started = session.TimeProvider.GetTimestamp();
                    var frame = converter.Convert(presentation.PositionedFrame.Frame, conversion.Token);
                    var elapsed = MediaTime.FromTimeSpan(session.TimeProvider.GetElapsedTime(started));
                    PreparedVideoPreview prepared;
                    bool catchup;
                    lock (gate)
                    {
                        ObservePreparationCostUnderLock(run, session, elapsed, false);
                        if (!IsPresentationCurrentUnderLock(run, identity) || conversion.IsCancellationRequested)
                        {
                            continue;
                        }
                        prepared = new(identity, frame, converter.GetRetainedBytes(frame));
                        catchup = identity.NextTime is { } next && next - identity.Time <= run.ConversionLead + run.DispatchLead;
                        if (prepared.Bytes < frame.Pixels.Length || prepared.Bytes > MAXIMUM_PREPARED_BYTES / 2 ||
                            run.PreparedBytes + prepared.Bytes > MAXIMUM_PREPARED_BYTES)
                        {
                            throw new NotSupportedException("预览图像超过提前准备的内存预算。");
                        }
                        run.PreparedFrames.Enqueue(prepared);
                        run.PreparedFrameCount++;
                        run.PreparedBytes += prepared.Bytes;
                        queued = true;
                        PulsePreparedUnderLock(run);
                    }
                    if (catchup)
                    {
                        await prepared.Completion.Task.WaitAsync(conversion.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                }
                catch (OperationCanceledException) when (conversion.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
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
            finally
            {
                if (!queued)
                {
                    run.PreparedSlots.Release();
                }
            }
        }
    }

    private async Task PresentPreparedFramesAsync(VideoPreviewRun run, VideoPlaybackSession session,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PreparedVideoPreview? prepared;
            Task changed;
            lock (gate)
            {
                run.PreparedFrames.TryDequeue(out prepared);
                changed = run.PreparedChanged.Task;
            }
            if (prepared is null)
            {
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, prepared.Identity.PreparationToken);
            try
            {
                lock (gate)
                {
                    if (!IsPresentationCurrentUnderLock(run, prepared.Identity))
                    {
                        continue;
                    }
                }
                await session.WaitForPositionAsync(prepared.Identity.Time, prepared.Identity.Generation, deliveryCancellation.Token).ConfigureAwait(false);
                await DispatchAsync(run, prepared.Identity, prepared.Frame, false, deliveryCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deliveryCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                lock (gate)
                {
                    run.PreparedFrameCount--;
                    run.PreparedBytes -= prepared.Bytes;
                }
                prepared.Completion.TrySetResult();
                run.PreparedSlots.Release();
            }
        }
    }

    private static async Task CancelCompanionOnExitAsync(Func<Task> action, CancellationTokenSource cancellation)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private static void PulsePreparedUnderLock(VideoPreviewRun run)
    {
        var previous = run.PreparedChanged;
        run.PreparedChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    private static void ClearPreparedFramesUnderLock(VideoPreviewRun? run)
    {
        if (run is null)
        {
            return;
        }
        run.PreparationCancellation.Cancel();
        run.PreparationCancellation.Dispose();
        run.PreparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        while (run.PreparedFrames.TryDequeue(out var prepared))
        {
            run.PreparedFrameCount--;
            run.PreparedBytes -= prepared.Bytes;
            prepared.Completion.TrySetResult();
            run.PreparedSlots.Release();
        }
        PulsePreparedUnderLock(run);
    }

    private static void ObservePreparationCostUnderLock(VideoPreviewRun run, VideoPlaybackSession session,
        MediaTime elapsed, bool dispatchCost)
    {
        var samples = dispatchCost ? run.DispatchCosts : run.ConversionCosts;
        samples.Enqueue(elapsed);
        while (samples.Count > PREPARATION_OBSERVATION_COUNT)
        {
            samples.Dequeue();
        }
        var maximum = samples.Max();
        if (dispatchCost)
        {
            run.DispatchLead = maximum;
        }
        else
        {
            run.ConversionLead = maximum;
        }
        session.SetPreparationLead(run.ConversionLead + run.DispatchLead);
    }
}
