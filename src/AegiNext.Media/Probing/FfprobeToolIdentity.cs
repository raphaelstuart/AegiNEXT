using System.Collections.Immutable;

namespace AegiNext.Media.Probing;

/// <summary>
/// 记录实际运行的工具文件及关键动态库身份；文件哈希不代替发布包签名校验。
/// </summary>
public sealed record FfprobeToolIdentity(string ExecutablePath, string Sha256, string Version,
    ImmutableDictionary<string, string> LibraryVersions);
