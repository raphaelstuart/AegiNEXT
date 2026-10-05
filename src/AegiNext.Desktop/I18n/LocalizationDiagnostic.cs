namespace AegiNext.Desktop.I18n;

/// <summary>记录被排除的语言资源及其加载原因。</summary>
public sealed record LocalizationDiagnostic(string FilePath, string Message);
