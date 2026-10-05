using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Controllers;

internal static class VideoPreviewProbe
{
    private static readonly string[] unsupportedSideData =
        ["dynamic", "dovi", "dolby", "stereo", "icc", "raw color", "film grain", "ambient", "display matrix"];

    internal static async Task<VideoPreviewMedia> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
        var report = await probe.ProbeAsync(filePath, cancellationToken).ConfigureAwait(false);
        var selected = report.Asset.Streams
            .Where(stream => stream.CodecType == "video" && stream.Video is not null && stream.Disposition.GetValueOrDefault("attached_pic") == 0)
            .OrderByDescending(stream => stream.Disposition.GetValueOrDefault("default"))
            .ThenBy(stream => stream.Index)
            .FirstOrDefault() ?? throw new InvalidDataException(Localization.Get("Preview.NoVideo"));
        if (!selected.Video!.DisplayMatrices.IsDefaultOrEmpty)
        {
            throw new NotSupportedException(Localization.Get("Preview.DisplayMatrix"));
        }

        foreach (var name in selected.Video.SideDataTypes)
        {
            if (unsupportedSideData.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NotSupportedException($"{Localization.Get("Preview.UnsupportedMetadata")} {name}");
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

}
