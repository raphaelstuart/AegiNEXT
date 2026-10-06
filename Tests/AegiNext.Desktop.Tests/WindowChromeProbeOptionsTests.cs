using AegiNext.Desktop.Diagnostics;

namespace AegiNext.Desktop.Tests;

public sealed class WindowChromeProbeOptionsTests
{
    [Theory]
    [InlineData()]
    [InlineData("--hdr-probe")]
    [InlineData("--hdr-probe-auto")]
    public void NormalAndHdrLaunchDoNotEnableChromeProbe(params string[] args)
    {
        var options = WindowChromeProbeOptions.Parse(args);

        Assert.False(options.Enabled);
        Assert.False(options.Automatic);
    }

    [Theory]
    [InlineData("--chrome-probe", false)]
    [InlineData("--chrome-probe-auto", true)]
    public void ExplicitChromeLaunchSelectsItsOwnDiagnosticMode(string argument, bool automatic)
    {
        var options = WindowChromeProbeOptions.Parse([argument]);

        Assert.True(options.Enabled);
        Assert.Equal(automatic, options.Automatic);
        Assert.True(Path.IsPathFullyQualified(options.ReportPath));
    }

    [Fact]
    public void ReportPathIsResolvedWithoutWritingAnyFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext.Chrome.Options", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "report.json");

        var options = WindowChromeProbeOptions.Parse(["--chrome-probe", "--chrome-probe-report", path]);

        Assert.Equal(path, options.ReportPath);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("--chrome-probe-report")]
    [InlineData("--chrome-probe-report", "")]
    [InlineData("--chrome-probe-report", "--chrome-probe-auto")]
    public void MissingReportPathIsRejected(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => WindowChromeProbeOptions.Parse(args));
    }

    [Theory]
    [InlineData("--chrome-probe", "--hdr-probe")]
    [InlineData("--chrome-probe-auto", "--hdr-probe")]
    [InlineData("--chrome-probe", "--hdr-probe-auto")]
    public void MutuallyExclusiveDiagnosticsCannotStartTogether(string chrome, string hdr)
    {
        Assert.Throws<ArgumentException>(() => WindowChromeProbeOptions.Parse([chrome, hdr]));
    }
}
