using System.Globalization;

namespace AegiNext.Desktop.Layouts;

internal static class LayoutText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries = new(StringComparer.Ordinal)
    {
        ["Layout"] = ("布局", "Layout"),
        ["standard"] = ("标准", "Standard"), ["timing"] = ("打轴", "Timing"),
        ["effects"] = ("特效", "Effects"), ["encode"] = ("压制", "Encoding"),
        ["preview"] = ("视频预览", "Video preview"), ["timeline"] = ("时间线", "Timeline"),
        ["subtitles"] = ("字幕列表", "Subtitles"), ["styles"] = ("样式", "Styles"),
        ["export"] = ("压制", "Encoding"),
        ["Modified"] = ("已修改", "Modified"), ["Save"] = ("保存布局", "Save layout"),
        ["SaveAs"] = ("布局另存为…", "Save layout as…"), ["Manage"] = ("管理布局…", "Manage layouts…"),
        ["Restore"] = ("恢复默认布局", "Restore default layout"), ["Name"] = ("布局名称", "Layout name"),
        ["Rename"] = ("重命名", "Rename"), ["Delete"] = ("删除", "Delete"),
        ["Apply"] = ("切换布局", "Switch layout"), ["Close"] = ("关闭", "Close"),
        ["Cancel"] = ("取消", "Cancel"), ["BuiltIn"] = ("内置", "Built-in"),
        ["InvalidName"] = ("请输入不重复的布局名称（1–80 个字符）。", "Enter a unique layout name (1–80 characters)."),
        ["ReadOnly"] = ("内置布局为只读，请另存为个人布局。", "Built-in layouts are read-only. Save a personal copy."),
        ["InvalidDraft"] = ("请先修正未提交的输入。", "Correct the pending input before switching layouts."),
        ["Corrupt"] = ("布局无法恢复，已保留诊断并恢复标准布局。", "The layout could not be restored. Diagnostics were retained and the standard layout was loaded.")
    };

    internal static string Get(string key, CultureInfo culture)
    {
        return entries.TryGetValue(key, out var text)
            ? culture.TwoLetterISOLanguageName == "zh" ? text.Chinese : text.English
            : key;
    }
}
