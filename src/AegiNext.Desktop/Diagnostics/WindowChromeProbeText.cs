using System.Globalization;

namespace AegiNext.Desktop.Diagnostics;

internal static class WindowChromeProbeText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries = new(StringComparer.Ordinal)
    {
        ["Main"] = ("主窗口", "Main window"),
        ["Floating"] = ("浮动面板", "Floating panel"),
        ["Settings"] = ("设置窗口", "Settings window"),
        ["Window"] = ("窗口", "Window"),
        ["WindowMenu"] = ("使用窗口菜单", "Use window menu"),
        ["Resize"] = ("重复恢复客户区尺寸", "Restore client size repeatedly"),
        ["Children"] = ("显示浮窗与设置窗", "Show floating and settings windows"),
        ["RejectClose"] = ("取消下一次关闭", "Cancel the next close request"),
        ["CloseCancelled"] = ("关闭已取消，窗口仍可交互。", "Close cancelled; the window remains interactive."),
        ["Description"] = ("检查系统按钮、空白标题拖动、菜单点击、全屏和尺寸恢复。此窗口不加载工程或个人设置。",
            "Check system buttons, title dragging, menus, full screen and size restoration. This window does not load a project or personal preferences."),
        ["Close"] = ("关闭", "Close"),
        ["Probe"] = ("标题栏验证", "Window chrome probe")
    };

    internal static string Get(string key)
    {
        var value = entries[key];
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? value.Chinese : value.English;
    }
}
