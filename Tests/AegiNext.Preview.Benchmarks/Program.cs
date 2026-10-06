using AegiNext.Preview.Benchmarks;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;

var source = Path.GetFullPath(args[0]);
var reportPath = Path.GetFullPath(args[1]);
var interactive = args.Contains("interactive", StringComparer.Ordinal);
var scrub = args.Contains("scrub", StringComparer.Ordinal);
var modeArgument = args.FirstOrDefault(value => value.StartsWith("decode=", StringComparison.OrdinalIgnoreCase));
var mode = modeArgument is null ? VideoDecodeMode.Software : Enum.Parse<VideoDecodeMode>(modeArgument[7..], true);
var roundsArgument = args.FirstOrDefault(value => value.StartsWith("rounds=", StringComparison.OrdinalIgnoreCase));
var rounds = roundsArgument is null ? 1 : int.Parse(roundsArgument[7..], System.Globalization.CultureInfo.InvariantCulture);
if (rounds is < 1 or > 20)
{
    throw new ArgumentOutOfRangeException(nameof(args), "rounds must be between 1 and 20.");
}
var document = new ProjectDocument { Width = 3840, Height = 2160 };
var subtitles = ImmutableArray.CreateBuilder<SubtitleLine>();
var layers = ImmutableArray.CreateBuilder<ProjectLayer>();
var tracks = ImmutableArray.CreateBuilder<SubtitleTrack>();
for (var index = 0; index < 4; index++)
{
    var track = new SubtitleTrack { Name = "Track " + index };
    var cue = new SubtitleLine
    {
        TrackId = track.Id, End = new(600), Text = "Animated natural subtitle " + index,
        Style = new() { FontFamily = "Arial", FontSize = 84, Alignment = TextAlignment.BOTTOM_CENTER }
    };
    tracks.Add(track);
    subtitles.Add(cue);
    layers.Add(new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End, Transform = new(Y: -index * 140),
        Tracks = [new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(-120, -index * 140)), new(new(600), new ScenePoint(120, -index * 140))])]
    });
}
document = document with { SubtitleTracks = tracks.ToImmutable(), Subtitles = subtitles.ToImmutable(), Layers = layers.ToImmutable() };
var observations = new List<PreviewBenchmarkSample>();
var initialization = new List<double>();
var sessions = new List<VideoDecodeSessionInfo>();
using var converter = new SdrVideoConverter(interactive ? new(960, 540) : new());
using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(Path.GetDirectoryName(source)!));
for (var round = 0; round < rounds; round++)
{
    var initializationStarted = Stopwatch.GetTimestamp();
    using var navigator = VideoFrameNavigator.Open(source, 0, new VideoDecoderOptions { Mode = mode });
    initialization.Add(Stopwatch.GetElapsedTime(initializationStarted).TotalMilliseconds);
    for (var index = 0; index < 32; index++)
    {
        var target = new MediaTime(120 + (scrub ? index * 37 % 90 : index), 30);
        var before = navigator.SessionInfo!;
        var started = Stopwatch.GetTimestamp();
        using var frame = navigator.SeekFrame(target) ?? throw new InvalidDataException("No video frame.");
        var decoded = Stopwatch.GetTimestamp();
        var after = navigator.SessionInfo!;
        var background = converter.Convert(frame.Frame);
        var converted = Stopwatch.GetTimestamp();
        var scale = Math.Min((double)background.Width / document.Width, (double)background.Height / document.Height);
        var width = (int)Math.Round(document.Width * scale);
        var height = (int)Math.Round(document.Height * scale);
        var composed = renderer.ComposePreview(document, target, background.Pixels.Span, background.Width, background.Height,
            background.Width * 4, width, height);
        var complete = Stopwatch.GetTimestamp();
        if (composed.Length != width * height * 4)
        {
            throw new InvalidDataException("Invalid composed size.");
        }
        observations.Add(new(round, index, target.ToString(), frame.Time.ToString(), width, height,
            Stopwatch.GetElapsedTime(started, decoded).TotalMilliseconds,
            Stopwatch.GetElapsedTime(decoded, converted).TotalMilliseconds,
            Stopwatch.GetElapsedTime(converted, complete).TotalMilliseconds,
            Stopwatch.GetElapsedTime(started, complete).TotalMilliseconds,
            (after.DecodeNanoseconds >= before.DecodeNanoseconds
                ? after.DecodeNanoseconds - before.DecodeNanoseconds : after.DecodeNanoseconds) / 1_000_000d,
            (after.DownloadNanoseconds >= before.DownloadNanoseconds
                ? after.DownloadNanoseconds - before.DownloadNanoseconds : after.DownloadNanoseconds) / 1_000_000d));
    }
    sessions.Add(navigator.SessionInfo!);
}
var warm = observations.Where(sample => sample.Index > 0).ToArray();
var report = new
{
    Source = source, DecodeMode = mode.ToString(), Rounds = rounds, Sessions = sessions, Initialization = Percentiles(initialization), ProjectWidth = document.Width, ProjectHeight = document.Height, TrackCount = tracks.Count,
    Strategy = interactive ? "interactive-960x540" : "precise-1280x720",
    Navigation = scrub ? "alternating seeks within 4-7 seconds" : "consecutive seeks within 4-5 seconds",
    Includes = "real native decode, SDR conversion, four animated subtitle tracks and CPU scene composition; excludes UI upload. Codec/download are accumulated API stage durations; GPU work may finish during download.",
    Decode = Percentiles(warm.Select(sample => sample.DecodeMilliseconds)),
    Codec = Percentiles(warm.Select(sample => sample.CodecMilliseconds)),
    Download = Percentiles(warm.Select(sample => sample.DownloadMilliseconds)),
    Convert = Percentiles(warm.Select(sample => sample.ConvertMilliseconds)),
    Compose = Percentiles(warm.Select(sample => sample.ComposeMilliseconds)),
    Total = Percentiles(warm.Select(sample => sample.TotalMilliseconds)), Samples = observations
};
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.DecodeMode, report.Rounds, report.Sessions, report.Initialization, report.Strategy, report.Decode, report.Codec, report.Download, report.Convert, report.Compose, report.Total }));

static object Percentiles(IEnumerable<double> samples)
{
    var ordered = samples.Order().ToArray();
    return new { P50 = ordered[ordered.Length / 2], P95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1] };
}
