namespace AegiNext.Desktop.Diagnostics;

internal sealed record HdrProbeOptions(bool Enabled, bool Automatic, string? FontPath, string ReportPath)
{
    internal static HdrProbeOptions Parse(string[] args)
    {
        var enabled = args.Contains("--hdr-probe", StringComparer.Ordinal);
        var automatic = args.Contains("--hdr-probe-auto", StringComparer.Ordinal);
        string? font = null;
        var report = Path.Combine("artifacts", "verification", "step-1.3-hdr.json");
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--hdr-probe-font" or "--hdr-probe-report"))
            {
                continue;
            }

            var option = args[i];
            if (++i == args.Length)
            {
                throw new ArgumentException($"{option} 缺少路径。");
            }

            if (option == "--hdr-probe-font")
            {
                font = Path.GetFullPath(args[i]);
            }
            else
            {
                report = args[i];
            }
        }

        return new(enabled || automatic, automatic, font, Path.GetFullPath(report));
    }
}
