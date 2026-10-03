namespace AegiNext.Media.Preview;

/// <summary>
/// 实际 SDR 转换后端的编译与运行版本。
/// </summary>
public sealed record SdrPreviewBackendInfo(string CompiledVersion, string RuntimeVersion);
