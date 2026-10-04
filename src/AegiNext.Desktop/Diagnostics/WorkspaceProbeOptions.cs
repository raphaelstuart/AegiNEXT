namespace AegiNext.Desktop.Diagnostics;

internal sealed record WorkspaceProbeOptions(bool Enabled, string ReportPath, string ProfileDirectory,
    string? SourceProfileDirectory = null)
{
    internal static WorkspaceProbeOptions Parse(string[] args)
    {
        var enabled = args.Contains("--workspace-probe-auto", StringComparer.Ordinal);
        var report = Path.GetFullPath(Path.Combine("artifacts", "verification", "workspace-probe.json"));
        string? sourceProfile = null;
        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (option is not ("--workspace-probe-report" or "--workspace-probe-source-profile"))
            {
                continue;
            }
            if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} 缺少路径。", nameof(args));
            }
            if (option == "--workspace-probe-report")
            {
                report = Path.GetFullPath(args[i]);
            }
            else
            {
                sourceProfile = Path.GetFullPath(args[i]);
            }
        }
        if (enabled && args.Any(argument => argument is "--chrome-probe" or "--chrome-probe-auto" or "--hdr-probe" or "--hdr-probe-auto"))
        {
            throw new ArgumentException("工作台、标题栏和 HDR 诊断必须分别启动。", nameof(args));
        }
        if (sourceProfile is not null && !enabled)
        {
            throw new ArgumentException("复制诊断偏好必须同时启用 --workspace-probe-auto。", nameof(args));
        }
        return new(enabled, report, Path.Combine(Path.GetTempPath(), "AegiNext.Workspace.Probe", Guid.NewGuid().ToString("N")), sourceProfile);
    }
}
