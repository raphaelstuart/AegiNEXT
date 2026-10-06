using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Tests.Windowing;

public sealed class WindowsChromeInteropLayoutTests
{
    [Fact]
    public void NativeValueTypesMatchWindowsSdkStructureSizes()
    {
        Assert.Equal(8, Marshal.SizeOf<WindowsPoint>());
        Assert.Equal(16, Marshal.SizeOf<WindowsRect>());
        Assert.Equal(16, Marshal.SizeOf<WindowsMargins>());
        Assert.Equal(40, Marshal.SizeOf<WindowsMinMaxInfo>());
        Assert.Equal(44, Marshal.SizeOf<WindowsWindowPlacement>());
        Assert.Equal(140, Marshal.SizeOf<WindowsTitleBarInfoEx>());
        Assert.Equal(48 + IntPtr.Size, Marshal.SizeOf<WindowsNccalcSizeParameters>());
    }

    [Fact]
    public void HookParametersContainNoManagedReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsRect>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsMinMaxInfo>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsNccalcSizeParameters>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsWindowPlacement>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsTitleBarInfoEx>());
    }
}
