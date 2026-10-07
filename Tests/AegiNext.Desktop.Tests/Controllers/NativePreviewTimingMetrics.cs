using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class NativePreviewTimingMetrics
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private long playbackStarted;
    private long playbackEnded;

    internal ConcurrentQueue<double> ReadMilliseconds { get; } = new();
    internal ConcurrentQueue<double> SeekMilliseconds { get; } = new();
    internal ConcurrentQueue<double> NativeDecodeMillisecondsPerRead { get; } = new();
    internal ConcurrentQueue<double> NativeDownloadMillisecondsPerRead { get; } = new();
    internal ConcurrentQueue<double> ConversionMilliseconds { get; } = new();
    internal ConcurrentQueue<double> UiQueueMilliseconds { get; } = new();
    internal ConcurrentQueue<(long Timestamp, VideoPreviewSnapshot Snapshot)> Presentations { get; } = new();
    internal ConcurrentQueue<(int Width, int Height)> PresentedDimensions { get; } = new();
    internal long PlaybackMidpoint => playbackStarted + (playbackEnded - playbackStarted) / 2;

    internal void BeginPlayback()
    {
        playbackStarted = Stopwatch.GetTimestamp();
        playbackEnded = playbackStarted + Stopwatch.Frequency * 2;
    }

    internal void EndPlayback()
    {
        if (playbackStarted != 0)
        {
            playbackEnded = Math.Min(Stopwatch.GetTimestamp(), playbackStarted + Stopwatch.Frequency * 2);
        }
    }

    internal (long Timestamp, VideoPreviewSnapshot Snapshot)[] GetPlayingPresentations()
    {
        return Presentations.Where(sample => sample.Timestamp >= playbackStarted &&
            sample.Timestamp <= playbackEnded && sample.Snapshot.State == VideoPlaybackState.PLAYING).ToArray();
    }

    internal async Task WriteReportAsync(VideoDecodeMode mode, VideoDecodeSessionInfo? decoder,
        VideoPreviewSnapshot? endingSnapshot, string? failure,
        (uint Decoders, uint Frames, uint Converters) initialResources,
        (uint Decoders, uint Frames, uint Converters) finalResources)
    {
        var playing = GetPlayingPresentations();
        var elapsed = playbackStarted != 0 && playbackEnded != 0 ? Stopwatch.GetElapsedTime(playbackStarted, playbackEnded).TotalSeconds : 0;
        var intervals = playing.Zip(playing.Skip(1), (first, second) =>
            Stopwatch.GetElapsedTime(first.Timestamp, second.Timestamp).TotalMilliseconds).ToArray();
        var midpoint = PlaybackMidpoint;
        var report = new
        {
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            OperatingSystem = RuntimeInformation.OSDescription,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Fixture = new { Codec = "h264", Width = 1920, Height = 1080, FramesPerSecond = 60, DurationSeconds = 6, Color = "bt709 limited", AudioStreams = 0 },
            RequestedMode = mode.ToString(),
            ActualDecoder = decoder is null ? null : new
            {
                Backend = decoder.ActiveBackend.ToString(), decoder.HardwareConfirmed, decoder.FallbackReason,
                decoder.DeliveredFrames, decoder.DecodeNanoseconds, decoder.DownloadNanoseconds
            },
            BackendObservation = "Measured directly from the owned native navigator; the source measurement wrapper does not expose a controller backend badge.",
            ConversionOutput = new { Width = 1920, Height = 1080 },
            SimulatedUiQueueMilliseconds = 8,
            PlaybackSeconds = elapsed,
            PresentedFrames = playing.Length,
            PresentedFramesPerSecond = elapsed > 0 ? playing.Length / elapsed : 0,
            FirstHalfPresentedFrames = playing.Count(sample => sample.Timestamp <= midpoint),
            SecondHalfPresentedFrames = playing.Count(sample => sample.Timestamp > midpoint),
            ValidPresentedIntervals = playing.Count(sample => sample.Snapshot.PresentedFrameTime <= sample.Snapshot.PresentedAtPosition &&
                sample.Snapshot.PresentedAtPosition < sample.Snapshot.PresentedFrameEnd),
            EndingState = endingSnapshot?.State.ToString(),
            EndingPosition = endingSnapshot?.Position.ToString(),
            StagesIncludeOpeningAndPlayback = true,
            Stages = new
            {
                SourceRead = Summarize(ReadMilliseconds),
                SourceSeek = Summarize(SeekMilliseconds),
                NativeDecodePerSourceRead = Summarize(NativeDecodeMillisecondsPerRead),
                NativeDownloadPerSourceRead = Summarize(NativeDownloadMillisecondsPerRead),
                SdrConversion = Summarize(ConversionMilliseconds),
                UiQueue = Summarize(UiQueueMilliseconds),
                PresentedFrameIntervals = Summarize(intervals)
            },
            NativeResourcesBefore = new { initialResources.Decoders, initialResources.Frames, initialResources.Converters },
            NativeResourcesAfter = new { finalResources.Decoders, finalResources.Frames, finalResources.Converters },
            Failure = failure
        };
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "AegiNext.sln")))
        {
            root = root.Parent ?? throw new DirectoryNotFoundException("Cannot locate the AegiNext validation artifact directory.");
        }
        var directory = Path.Combine(root.FullName, "artifacts", "validation", "timing");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"preview-native-{RuntimeInformation.RuntimeIdentifier}-{mode.ToString().ToLowerInvariant()}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, jsonOptions));
    }

    private static object Summarize(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return new
        {
            Samples = ordered.Length,
            P50Milliseconds = ordered.Length == 0 ? (double?)null : ordered[(int)Math.Ceiling(ordered.Length * 0.5) - 1],
            P95Milliseconds = ordered.Length == 0 ? (double?)null : ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
            MaximumMilliseconds = ordered.Length == 0 ? (double?)null : ordered[^1]
        };
    }
}
