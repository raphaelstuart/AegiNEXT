namespace AegiNext.Desktop.Diagnostics;

internal sealed record WorkspaceProbeOptions(bool Enabled, string ReportPath, string ProfileDirectory)
{
    internal static WorkspaceProbeOptions Parse(string[] args)
    {
        var enabled = args.Contains("--workspace-probe-auto", StringComparer.Ordinal);
        var report = Path.GetFullPath(Path.Combine("artifacts", "verification", "workspace-probe.json"));
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != "--workspace-probe-report")
            {
                continue;
            }
            if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("--workspace-probe-report 缺少报告路径。", nameof(args));
            }
            report = Path.GetFullPath(args[i]);
        }
        if (enabled && args.Any(argument => argument is "--chrome-probe" or "--chrome-probe-auto" or "--hdr-probe" or "--hdr-probe-auto"))
        {
            throw new ArgumentException("工作台、标题栏和 HDR 诊断必须分别启动。", nameof(args));
        }
        return new(enabled, report, Path.Combine(Path.GetTempPath(), "AegiNext.Workspace.Probe", Guid.NewGuid().ToString("N")));
    }
}
