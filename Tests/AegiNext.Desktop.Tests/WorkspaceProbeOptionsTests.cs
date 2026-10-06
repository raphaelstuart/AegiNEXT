using AegiNext.Desktop.Diagnostics;

namespace AegiNext.Desktop.Tests;

public sealed class WorkspaceProbeOptionsTests
{
    [Fact]
    public void NormalStartupDoesNotEnableTheProbe()
    {
        Assert.False(WorkspaceProbeOptions.Parse([]).Enabled);
    }

    [Fact]
    public void AutomaticProbeUsesAnIndependentTemporaryProfileAndAnAbsoluteReport()
    {
        var options = WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-report", "probe-report.json"]);
        Assert.True(options.Enabled);
        Assert.Equal(Path.GetFullPath("probe-report.json"), options.ReportPath);
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "AegiNext.Workspace.Probe"), options.ProfileDirectory, StringComparison.Ordinal);
        Assert.False(Directory.Exists(options.ProfileDirectory));
        Assert.Null(options.SourceProfileDirectory);
    }

    [Fact]
    public void SourceProfileIsExplicitAndNeverBecomesTheWritableProbeDirectory()
    {
        var options = WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-source-profile", "source-profile"]);
        Assert.Equal(Path.GetFullPath("source-profile"), options.SourceProfileDirectory);
        Assert.NotEqual(options.SourceProfileDirectory, options.ProfileDirectory);
        Assert.False(Directory.Exists(options.ProfileDirectory));
    }

    [Fact]
    public void SourceProfileRequiresAnEnabledProbe()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProbeOptions.Parse(["--workspace-probe-source-profile", "source-profile"]));
    }

    [Theory]
    [InlineData("--chrome-probe")]
    [InlineData("--chrome-probe-auto")]
    [InlineData("--hdr-probe")]
    [InlineData("--hdr-probe-auto")]
    public void ProbeCannotShareAProcessWithOtherDiagnosticLifetimes(string conflictingFlag)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProbeOptions.Parse(["--workspace-probe-auto", conflictingFlag]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--workspace-probe-auto")]
    public void EmptyOrFlagReportPathIsRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProbeOptions.Parse(["--workspace-probe-report", path]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--workspace-probe-auto")]
    public void EmptyOrFlagSourceProfilePathIsRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceProbeOptions.Parse(["--workspace-probe-auto", "--workspace-probe-source-profile", path]));
    }
}
