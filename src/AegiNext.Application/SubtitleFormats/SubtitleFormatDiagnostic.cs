namespace AegiNext.Application.SubtitleFormats;

/// <summary>可继续操作的格式损失，附带来源位置及受影响字幕标识。</summary>
public sealed record SubtitleFormatDiagnostic(string Code, string Message, int SourceStart = 0,
    int SourceLength = 0, Guid? SubtitleId = null);
