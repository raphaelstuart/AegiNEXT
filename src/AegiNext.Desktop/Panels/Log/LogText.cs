using System.Globalization;

namespace AegiNext.Desktop.Panels.Log;

internal static class LogText
{
    private static readonly Dictionary<string, (string Chinese, string English)> entries = new(StringComparer.Ordinal)
    {
        ["All"] = ("所有级别", "All levels"), ["Info"] = ("信息", "Info"),
        ["Warning"] = ("警告", "Warning"), ["Error"] = ("错误", "Error"),
        ["Search"] = ("筛选消息、来源或详情", "Filter messages, sources or details"),
        ["CopySelected"] = ("复制所选", "Copy selected"), ["CopyVisible"] = ("复制筛选结果", "Copy filtered"),
        ["Clear"] = ("清空日志", "Clear log"),
        ["Empty"] = ("当前筛选没有日志。", "No log entries match the filter.")
    };

    internal static string Get(string key)
    {
        var value = entries[key];
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? value.Chinese : value.English;
    }
}
