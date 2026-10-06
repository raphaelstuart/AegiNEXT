using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Controllers;

internal sealed class VideoPreviewRun : IDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private VideoPlaybackSession? session;
    private AudioPlaybackSession? audio;
    private Task? stopTask;
    private bool disposed;

    internal VideoPreviewRun(long epoch, string path)
    {
        Epoch = epoch;
        Path = path;
        Token = cancellation.Token;
        PreparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
    }

    internal long Epoch { get; }
    internal string Path { get; }
    internal TaskCompletionSource<bool> FirstPresentation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token { get; }
    internal VideoPreviewMedia? Media { get; set; }
    internal Exception? Error { get; set; }
    internal Exception? AudioError { get; set; }
    internal MediaTime? PresentedFrameTime { get; set; }
    internal MediaTime? PresentedAtPosition { get; set; }
    internal long? PresentedGeneration { get; set; }
    internal CancellationTokenSource? ConversionCancellation { get; set; }
    internal CancellationTokenSource PreparationCancellation { get; set; }
    internal SemaphoreSlim PreparedSlots { get; } = new(2, 2);
    internal Queue<PreparedVideoPreview> PreparedFrames { get; } = new();
    internal TaskCompletionSource PreparedChanged { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Queue<MediaTime> ConversionCosts { get; } = new();
    internal Queue<MediaTime> DispatchCosts { get; } = new();
    internal MediaTime ConversionLead { get; set; }
    internal MediaTime DispatchLead { get; set; }
    internal int PreparedFrameCount { get; set; }
    internal long PreparedBytes { get; set; }
    internal MediaTime? PresentedFrameEnd { get; set; }
    internal Task Pump { get; set; } = Task.CompletedTask;
    internal TaskCompletionSource<bool> Resume { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal VideoPlaybackSession? Session
    {
        get
        {
            lock (gate)
            {
                return session;
            }
        }
    }

    internal AudioPlaybackSession? Audio
    {
        get
        {
            lock (gate)
            {
                return audio;
            }
        }
    }

    internal void AttachAudio(AudioPlaybackSession value)
    {
        lock (gate)
        {
            Token.ThrowIfCancellationRequested();
            audio = value;
        }
    }

    internal void Attach(VideoPlaybackSession value)
    {
        lock (gate)
        {
            Token.ThrowIfCancellationRequested();
            session = value;
        }
    }

    internal Task Stop()
    {
        lock (gate)
        {
            if (stopTask is not null)
            {
                return stopTask;
            }

            cancellation.Cancel();
            FirstPresentation.TrySetCanceled(Token);
            stopTask = Task.WhenAll(session?.CloseAsync() ?? Task.CompletedTask, audio?.CloseAsync() ?? Task.CompletedTask);
            return stopTask;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                PreparationCancellation.Dispose();
                cancellation.Dispose();
                PreparedSlots.Dispose();
            }
        }
    }
}
