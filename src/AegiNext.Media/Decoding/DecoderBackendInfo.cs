namespace AegiNext.Media.Decoding;

/// <summary>
/// 已核验的原生解码库及实际运行库版本。
/// </summary>
public sealed record DecoderBackendInfo(string ReleaseVersion, Version AvFormatVersion, Version AvCodecVersion, Version AvUtilVersion);
