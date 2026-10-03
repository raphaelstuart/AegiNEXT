namespace AegiNext.Media.Decoding;

/// <summary>
/// 已核验的原生软件解码后端及实际运行库版本。
/// </summary>
public sealed record DecoderBackendInfo(string ReleaseVersion, Version AvFormatVersion, Version AvCodecVersion, Version AvUtilVersion);
