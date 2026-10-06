using System.Runtime.InteropServices;
using AegiNext.Media.Preview;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Preview;

public sealed class NativePreviewLayoutTests
{
    [Fact]
    public void ManagedStructuresMatchTheStableNativePreviewContract()
    {
        Assert.Equal(16, Marshal.SizeOf<NativePreviewBackendInfo>());
        Assert.Equal(8, Marshal.OffsetOf<NativePreviewBackendInfo>("compileSwscale").ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativePreviewBackendInfo>("runtimeSwscale").ToInt32());
        Assert.Equal(48, Marshal.SizeOf<NativePreviewRequest>());
        Assert.Equal(8, Marshal.OffsetOf<NativePreviewRequest>("width").ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<NativePreviewRequest>("height").ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<NativePreviewRequest>("colorRange").ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<NativePreviewRequest>("colorTransfer").ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<NativePreviewRequest>("chromaLocation").ToInt32());
        Assert.Equal(36, Marshal.OffsetOf<NativePreviewRequest>("alphaMode").ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<NativePreviewRequest>("flags").ToInt32());
        Assert.Equal(44, Marshal.OffsetOf<NativePreviewRequest>("reserved").ToInt32());
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public void BackendUsesThePinnedMatchingSwscaleBuildAndRuntime()
    {
        var backend = SdrVideoConverter.GetBackendInfo();

        Assert.Equal("10.1.102", backend.CompiledVersion);
        Assert.Equal(backend.CompiledVersion, backend.RuntimeVersion);
    }
}
