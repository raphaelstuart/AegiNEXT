namespace AegiNext.Desktop.Shortcuts;

/// <summary>规范化快捷键及使用它的两个命令，不依赖界面或本地化文案。</summary>
public sealed record ShortcutConflict(string Gesture, WorkbenchCommand FirstCommand, WorkbenchCommand SecondCommand);
