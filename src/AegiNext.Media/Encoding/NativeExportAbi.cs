using System.Runtime.InteropServices;

namespace AegiNext.Media.Encoding;

internal static class NativeExportAbi
{
    internal const uint VERSION = 2;
    internal const uint REQUEST_SIZE = 72;

    internal static void Validate(uint nativeVersion, string libraryPath)
    {
        var requestSize = Marshal.SizeOf<NativeExportRequest>();
        if (nativeVersion == VERSION && requestSize == REQUEST_SIZE)
        {
            return;
        }

        throw new NotSupportedException($"原生导出 ABI 不匹配：需要 ABI {VERSION}，实际 ABI {nativeVersion}；" +
            $"请求结构需要 {REQUEST_SIZE} 字节，实际 {requestSize} 字节；进程架构 {RuntimeInformation.ProcessArchitecture}；" +
            $"加载路径 {libraryPath}。请按当前 Debug/Release 配置运行 " +
            "pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Debug（或 Release），使用完整的同配置应用与 worker。");
    }
}
