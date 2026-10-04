using System.Reflection;
using System.Runtime.InteropServices;

namespace AegiNext.Media;

internal static class NativeMediaRuntime
{
    static NativeMediaRuntime()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMediaRuntime).Assembly, Resolve);
    }

    internal static void Initialize()
    {
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name is not ("aeginext_decode" or "aeginext_audio" or "aeginext_export" or "aeginext_media"))
        {
            return 0;
        }

        var filename = OperatingSystem.IsWindows() ? name + ".dll"
            : OperatingSystem.IsMacOS() ? "lib" + name + ".dylib" : "lib" + name + ".so";
        var path = Path.Combine(AppContext.BaseDirectory, filename);
        try
        {
            return NativeLibrary.Load(path);
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException)
        {
            var reason = !File.Exists(path) ? "运行时文件缺失"
                : error is BadImageFormatException ? "模块格式或架构与当前进程不匹配"
                : "文件已存在，但其依赖库缺失或无法加载";
            throw new DllNotFoundException($"原生媒体模块 {filename}：{reason}。进程架构 {RuntimeInformation.ProcessArchitecture}；加载路径 {path}。请使用同一平台/RID 的完整应用包。原始错误：{error.Message}", error);
        }
    }
}
