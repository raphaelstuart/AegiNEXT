using System.Globalization;

namespace AegiNext.Desktop.Workspace.Diagnostics;

internal static class WorkflowLogText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries = new(StringComparer.Ordinal)
    {
        ["ProjectCreated"] = ("已创建工程", "Project created"),
        ["ProjectOpened"] = ("已打开工程", "Project opened"),
        ["MediaOpened"] = ("已打开媒体", "Media opened"),
        ["SubtitlesImported"] = ("已导入字幕", "Subtitles imported"),
        ["SubtitlesExported"] = ("已导出字幕文件", "Subtitle file exported"),
        ["AudioAnalysisCompleted"] = ("音频分析完成", "Audio analysis complete"),
        ["AudioAnalysisSkipped"] = ("没有可分析的音轨，已跳过音频分析", "Audio analysis skipped because no audio stream is available"),
        ["StyleLibraryLoaded"] = ("字幕样式库已加载", "Subtitle style library loaded"),
        ["StyleSaved"] = ("字幕样式已保存", "Subtitle style saved"),
        ["StyleDeleted"] = ("字幕样式已删除", "Subtitle style deleted"),
        ["StyleApplied"] = ("字幕样式已应用", "Subtitle style applied"),
        ["StylesImported"] = ("字幕样式已导入", "Subtitle styles imported"),
        ["StylesExported"] = ("字幕样式已导出", "Subtitle styles exported")
    };

    internal static string Get(string key, CultureInfo culture)
    {
        var entry = entries[key];
        return culture.TwoLetterISOLanguageName == "zh" ? entry.Chinese : entry.English;
    }
}
