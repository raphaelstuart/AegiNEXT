using System.Globalization;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Localization;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Controllers;

internal static class VideoPreviewProbe
{
    private static readonly string[] unsupportedSideData =
        ["dynamic", "dovi", "dolby", "stereo", "icc", "raw color", "film grain", "ambient", "display matrix"];

    internal static async Task<VideoPreviewMedia> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentUICulture;
        var probe = new FfprobeMediaProbe(new(ResolveTool(culture)));
        var report = await probe.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        var selected = report.Asset.Streams
            .Where(stream => stream.CodecType == "video" && stream.Video is not null && stream.Disposition.GetValueOrDefault("attached_pic") == 0)
            .OrderByDescending(stream => stream.Disposition.GetValueOrDefault("default"))
            .ThenBy(stream => stream.Index)
            .FirstOrDefault() ?? throw new InvalidDataException(PreviewText.Get("NoVideo", culture));
        if (!selected.Video!.DisplayMatrices.IsDefaultOrEmpty)
        {
            throw new NotSupportedException(PreviewText.Get("DisplayMatrix", culture));
        }

        foreach (var name in selected.Video.SideDataTypes)
        {
            if (unsupportedSideData.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"{PreviewText.Get("UnsupportedMetadata", culture)} {name}");
            }
        }

        var timing = selected.Timing;
        var start = timing.StartTimestamp?.ToMediaTime() ?? timing.ReportedStart ?? report.Asset.ReportedStart;
        var duration = ReadDuration(timing) ?? report.Asset.ReportedDuration;
        var audio = report.Asset.Streams.Where(stream => stream.CodecType == "audio")
            .OrderByDescending(stream => stream.Disposition.GetValueOrDefault("default"))
            .ThenBy(stream => stream.Index).FirstOrDefault();
        return new(selected.Index, start, duration is { } value && value > MediaTime.Zero ? value : null, audio?.Index,
            selected.Video.Width, selected.Video.Height, selected.Video.AverageFrameRate ?? selected.Video.FrameRate);
    }

    private static MediaTime? ReadDuration(MediaStreamTiming timing)
    {
        if (timing.DurationTicks is > 0 && timing.TimeBase is { } timeBase)
        {
            return new MediaTimestamp(timing.DurationTicks.Value, timeBase).ToMediaTime();
        }

        return timing.ReportedDuration;
    }

    private static string ResolveTool(CultureInfo culture)
    {
        var configured = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
            {
                throw new FileNotFoundException(PreviewText.Get("ConfiguredProbeMissing", culture), configured);
            }

            return Path.GetFullPath(configured);
        }

        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var adjacent = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(adjacent))
        {
            return adjacent;
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Take(256))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(PreviewText.Get("ProbeMissing", culture));
    }
}
