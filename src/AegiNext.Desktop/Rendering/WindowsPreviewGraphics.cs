using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using static Avalonia.OpenGL.Egl.EglConsts;

namespace AegiNext.Desktop.Rendering;

/// <summary>为默认 ANGLE 单上下文后端创建独立 D3D11 设备；生命周期不依赖窗口渲染器。</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsPreviewGraphics
{
    // ANGLE 同时由 Avalonia 使用，库句柄保留到进程结束；设备、display 和 context 都按预览实例释放。
    private static readonly Lazy<EglInterface> egl = new(() => new EglInterface(EglGetProcAddress));
    private static readonly GlVersion[] profiles = [new(GlProfileType.OpenGLES, 3, 0)];

    internal static unsafe EglDisplay CreateDisplay()
    {
        uint[] levels = [0xb100, 0xb000, 0xa100, 0xa000];
        nint device;
        int result;
        fixed (uint* requested = levels)
        {
            result = D3D11CreateDevice(0, 1, 0, 0, requested, (uint)levels.Length, 7, out device, out _, null);
        }
        if (result < 0 || device == 0)
        {
            throw new OpenGlException($"无法创建预览 D3D11 硬件设备：0x{result:X8}。");
        }

        nint angleDevice = 0;
        nint display = 0;
        var transferred = false;
        try
        {
            var api = egl.Value;
            var create = (delegate* unmanaged[Stdcall]<int, nint, nint, nint>)EglGetProcAddress("eglCreateDeviceANGLE");
            var release = (delegate* unmanaged[Stdcall]<nint, void>)EglGetProcAddress("eglReleaseDeviceANGLE");
            if (create == null || release == null)
            {
                throw new NotSupportedException("ANGLE 未提供独立 D3D11 设备接口。");
            }
            angleDevice = create(EGL_D3D11_DEVICE_ANGLE, device, 0);
            if (angleDevice == 0)
            {
                throw OpenGlException.GetFormattedException("eglCreateDeviceANGLE", api);
            }
            display = api.GetPlatformDisplayExt(EGL_PLATFORM_DEVICE_EXT, angleDevice, null);
            if (display == 0)
            {
                throw OpenGlException.GetFormattedException("eglGetPlatformDisplayEXT", api);
            }
            var ownedDevice = device;
            var ownedAngleDevice = angleDevice;
            var releaseAddress = (nint)release;
            var ownedDisplay = new EglDisplay(display, new EglDisplayOptions
            {
                Egl = api, GlVersions = profiles, ContextLossIsDisplayLoss = true,
                DisposeCallback = () =>
                {
                    ((delegate* unmanaged[Stdcall]<nint, void>)releaseAddress)(ownedAngleDevice);
                    Marshal.Release(ownedDevice);
                }
            });
            transferred = true;
            return ownedDisplay;
        }
        finally
        {
            if (!transferred)
            {
                if (display != 0)
                {
                    egl.Value.Terminate(display);
                }
                if (angleDevice != 0)
                {
                    var release = (delegate* unmanaged[Stdcall]<nint, void>)EglGetProcAddress("eglReleaseDeviceANGLE");
                    release(angleDevice);
                }
                Marshal.Release(device);
            }
        }
    }

    [LibraryImport("av_libGLESv2.dll", EntryPoint = "EGL_GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint EglGetProcAddress(string name);

    [LibraryImport("d3d11.dll")]
    private static unsafe partial int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        uint* featureLevels, uint featureLevelCount, uint sdkVersion, out nint device, out uint featureLevel, nint* immediateContext);
}
