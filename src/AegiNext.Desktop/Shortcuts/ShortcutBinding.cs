namespace AegiNext.Desktop.Shortcuts;

/// <summary>一个命令的可持久化快捷键；空字符串禁用，CmdOrCtrl 按平台解析。</summary>
public sealed record ShortcutBinding(WorkbenchCommand Command, string Gesture);
