using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Probing;

internal static class FfprobeVideoTimingJsonReader
{
    internal static VideoTimingIndex Read(string json, int streamIndex, MediaTime origin,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireObject(root, "root");
            var streams = RequireArray(root, "streams");
            if (streams.GetArrayLength() != 1)
            {
                throw Invalid("streams", "必须包含唯一的目标视频流。");
            }

            var stream = streams[0];
            RequireObject(stream, "stream");
            if (ReadInt64(stream, "index") != streamIndex ||
                !stream.TryGetProperty("codec_type", out var codecType) || codecType.GetString() != "video")
            {
                throw Invalid("stream", "目标流索引或类型与请求不一致。");
            }

            var timeBase = ReadTimeBase(stream);
            var frames = RequireArray(root, "frames");
            var count = frames.GetArrayLength();
            if (count == 0 || count > VideoTimingIndex.MaximumFrameCount)
            {
                throw Invalid("frames", $"视频帧数必须在 1 至 {VideoTimingIndex.MaximumFrameCount} 之间。");
            }

            var frameTimes = ImmutableArray.CreateBuilder<MediaTime>(count);
            var keyframes = ImmutableArray.CreateBuilder<int>();
            foreach (var frame in frames.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireObject(frame, "frame");
                if (ReadInt64(frame, "stream_index") != streamIndex)
                {
                    throw Invalid("stream_index", "帧不属于目标视频流。");
                }

                var pts = ReadInt64(frame, "pts");
                if (frame.TryGetProperty("best_effort_timestamp", out var bestEffort) &&
                    bestEffort.ValueKind != JsonValueKind.Null && ReadInt64(frame, "best_effort_timestamp") != pts)
                {
                    throw Invalid("best_effort_timestamp", "显示时间与实际帧 PTS 不一致，不能建立精确索引。");
                }

                var time = new MediaTimestamp(pts, timeBase).ToMediaTime() - origin;
                if (frameTimes.Count > 0 && time <= frameTimes[^1])
                {
                    throw Invalid("pts", $"显示顺序第 {frameTimes.Count} 帧时间未严格递增；不能补帧或推算平均帧率。");
                }

                var keyframe = ReadInt64(frame, "key_frame");
                if (keyframe is not (0 or 1))
                {
                    throw Invalid("key_frame", "关键帧标记必须为 0 或 1。");
                }

                if (keyframe == 1)
                {
                    keyframes.Add(frameTimes.Count);
                }

                frameTimes.Add(time);
            }

            if (keyframes.Count == 0 || keyframes[^1] != count - 1)
            {
                keyframes.Add(count - 1);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(frameTimes.MoveToImmutable(), keyframes.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("FFprobe 视频帧时间报告无效或超出精确时间表示范围。", exception);
        }
    }

    private static MediaTimeBase ReadTimeBase(JsonElement stream)
    {
        if (!stream.TryGetProperty("time_base", out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw Invalid("time_base", "缺少视频流时基。");
        }

        var text = element.GetString()!;
        var separator = text.IndexOf('/');
        if (separator <= 0 || separator != text.LastIndexOf('/') ||
            !long.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator) ||
            !long.TryParse(text.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator) ||
            numerator <= 0 || denominator <= 0)
        {
            throw Invalid("time_base", "视频流时基必须是可精确表示的正有理数。");
        }

        return new(numerator, denominator);
    }

    private static long ReadInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            throw Invalid(name, "缺少实际帧字段，不能以估算时间替代。");
        }

        var text = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
        if (text is null || text.Length > 20 ||
            !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ||
            value == long.MinValue)
        {
            throw Invalid(name, "必须是有效的 Int64 整数且不能是未定义 PTS 标记。");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(name, "缺少数组。");
        }

        return element;
    }

    private static void RequireObject(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(name, "必须是对象。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw Invalid(property.Name, "同一对象中的字段重复。");
            }
        }
    }

    private static InvalidDataException Invalid(string field, string message)
    {
        return new($"FFprobe 视频帧字段 {field}：{message}");
    }
}
