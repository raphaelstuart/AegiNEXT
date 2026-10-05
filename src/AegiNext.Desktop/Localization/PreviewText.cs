using System.Globalization;

namespace AegiNext.Desktop.Localization;

internal static class PreviewText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Open"] = ("打开视频", "Open video"),
            ["Empty"] = ("打开视频开始预览", "Open a video to preview"),
            ["Preview"] = ("视频预览", "Video preview"),
            ["Mode"] = ("SDR · 无音频", "SDR · No audio"),
            ["Play"] = ("播放", "Play"),
            ["Pause"] = ("暂停", "Pause"),
            ["Mute"] = ("静音", "Mute"),
            ["Unmute"] = ("取消静音", "Unmute"),
            ["Volume"] = ("音量", "Volume"),
            ["Opening"] = ("正在打开…", "Opening…"),
            ["Ready"] = ("已暂停", "Paused"),
            ["Playing"] = ("播放中", "Playing"),
            ["Ended"] = ("播放结束", "Ended"),
            ["Unknown"] = ("未知时长", "Unknown duration"),
            ["Videos"] = ("视频文件", "Video files"),
            ["AllFiles"] = ("所有文件", "All files"),
            ["NoVideo"] = ("文件中没有可预览的视频轨。", "No playable video stream was found."),
            ["DisplayMatrix"] = ("当前预览暂不支持视频旋转或显示矩阵。", "Video rotation and display matrices are not supported by this preview yet."),
            ["UnsupportedMetadata"] = ("当前预览暂不支持以下视频信息：", "This video metadata is not supported by the preview yet:"),
            ["LocalFile"] = ("请选择本地视频文件。", "Choose a local video file."),
            ["ConfiguredProbeMissing"] = ("AEGINEXT_FFPROBE_PATH 必须指向存在的 ffprobe 完整路径。", "AEGINEXT_FFPROBE_PATH must point to an existing absolute ffprobe path."),
            ["ProbeMissing"] = ("未找到 ffprobe。请安装项目要求的 FFmpeg，或设置 AEGINEXT_FFPROBE_PATH。", "ffprobe was not found. Install the project FFmpeg toolchain or set AEGINEXT_FFPROBE_PATH."),
            ["Shortcuts"] = ("空格 播放 / 暂停 · ← → 5 秒", "Space Play / Pause · ← → 5 seconds")
        };

    internal static string Get(string key, CultureInfo culture)
    {
        var value = entries[key];
        return culture.TwoLetterISOLanguageName == "zh" ? value.Chinese : value.English;
    }
}
