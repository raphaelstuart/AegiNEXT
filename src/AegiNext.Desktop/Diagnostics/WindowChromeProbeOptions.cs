namespace AegiNext.Desktop.Diagnostics;

internal sealed record WindowChromeProbeOptions(bool Enabled, bool Automatic, string ReportPath)
{
    internal static WindowChromeProbeOptions Parse(string[] args)
    {
        var automatic = args.Contains("--chrome-probe-auto", StringComparer.Ordinal);
        var enabled = automatic || args.Contains("--chrome-probe", StringComparer.Ordinal);
        var report = Path.Combine("artifacts", "verification", "window-chrome-probe.json");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--chrome-probe-report")
            {
                continue;
            }

            if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("--chrome-probe-report 缺少报告路径。", nameof(args));
            }

            report = args[i];
        }

        if (enabled && (args.Contains("--hdr-probe", StringComparer.Ordinal) ||
                        args.Contains("--hdr-probe-auto", StringComparer.Ordinal)))
        {
            throw new ArgumentException("标题栏诊断和 HDR 诊断必须分别启动。", nameof(args));
        }

        return new(enabled, automatic, Path.GetFullPath(report));
    }
}
