using System.Runtime.InteropServices;
using AegiNext.Media.Encoding;

namespace AegiNext.Media.Tests.Encoding;

public sealed class NativeExportAbiTests
{
    [Fact]
    public void CurrentContractUsesTheCompleteHardwareRequestLayout()
    {
        Assert.Equal(3U, NativeExportAbi.VERSION);
        Assert.Equal(80, Marshal.SizeOf<NativeExportRequest>());
        Assert.Equal(64, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.EncodingMode)).ToInt32());
        Assert.Equal(68, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.VideoBitrate)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.DecodeMode)).ToInt32());
        Assert.Equal(76, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.DecodeReserved)).ToInt32());
        Assert.Equal(328, Marshal.SizeOf<NativeExportResultInfo>());
        Assert.Equal(72, Marshal.OffsetOf<NativeExportResultInfo>(nameof(NativeExportResultInfo.FallbackReason)).ToInt32());
        NativeExportAbi.Validate(3, Path.Combine(Path.GetTempPath(), "aeginext_export.dll"));
    }

    [Theory]
    [InlineData(0U, 1U, 15U, 15U)]
    [InlineData(1U, 2U, 15U, 15U)]
    [InlineData(1U, 1U, 3U, 3U)]
    [InlineData(1U, 1U, 7U, 7U)]
    [InlineData(1U, 1U, 15U, 7U)]
    public void SharedCoreVersionAndCapabilitiesMustMatch(uint exportVersion, uint decodeVersion, uint exportCapabilities, uint decodeCapabilities)
    {
        Assert.Throws<NotSupportedException>(() => NativeExportAbi.ValidateCore(exportVersion, decodeVersion,
            exportCapabilities, decodeCapabilities, Path.Combine(Path.GetTempPath(), "aeginext_export.dll")));
        NativeExportAbi.ValidateCore(1, 1, 15, 15, Path.Combine(Path.GetTempPath(), "aeginext_export.dll"));
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(1U)]
    [InlineData(2U)]
    public void MismatchIdentifiesActualLibraryAndMatchingConfigurationRecovery(uint version)
    {
        var library = Path.Combine(Path.GetTempPath(), "工程 native", "libaeginext_export.dylib");
        var error = Assert.Throws<NotSupportedException>(() => NativeExportAbi.Validate(version, library));
        Assert.Contains("需要 ABI 3", error.Message, StringComparison.Ordinal);
        Assert.Contains($"实际 ABI {version}", error.Message, StringComparison.Ordinal);
        Assert.Contains(library, error.Message, StringComparison.Ordinal);
        Assert.Contains("-Target Workbench -Configuration", error.Message, StringComparison.Ordinal);
    }
}
