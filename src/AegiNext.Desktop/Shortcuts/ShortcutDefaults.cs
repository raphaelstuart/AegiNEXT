using System.Collections.Immutable;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>可移植的默认应用快捷键，不注册操作系统全局热键。</summary>
public static class ShortcutDefaults
{
    /// <summary>返回所有命令的默认绑定；恢复默认不会修改调用者的旧数组。</summary>
    public static ImmutableArray<ShortcutBinding> CreateBindings()
    {
        return
        [
            new(WorkbenchCommand.NEW_PROJECT, "CmdOrCtrl+N"),
            new(WorkbenchCommand.OPEN_PROJECT, "CmdOrCtrl+O"),
            new(WorkbenchCommand.SAVE_PROJECT, "CmdOrCtrl+S"),
            new(WorkbenchCommand.SAVE_PROJECT_AS, "CmdOrCtrl+Shift+S"),
            new(WorkbenchCommand.OPEN_MEDIA, "CmdOrCtrl+Shift+O"),
            new(WorkbenchCommand.IMPORT_SUBTITLES, "CmdOrCtrl+I"),
            new(WorkbenchCommand.EXPORT_SUBTITLES, "CmdOrCtrl+Shift+E"),
            new(WorkbenchCommand.UNDO, "CmdOrCtrl+Z"),
            new(WorkbenchCommand.REDO, "CmdOrCtrl+Shift+Z"),
            new(WorkbenchCommand.OPEN_SETTINGS, "CmdOrCtrl+OemComma"),
            new(WorkbenchCommand.PLAY_PAUSE, "Space"),
            new(WorkbenchCommand.SEEK_BACKWARD, "Left"),
            new(WorkbenchCommand.SEEK_FORWARD, "Right"),
            new(WorkbenchCommand.TIMING_ENTER, "F8"),
            new(WorkbenchCommand.TIMING_EXIT, "F9"),
            new(WorkbenchCommand.ADD_SUBTITLE, "CmdOrCtrl+Enter"),
            new(WorkbenchCommand.DELETE_SUBTITLE, "Delete"),
            new(WorkbenchCommand.SPLIT_SUBTITLE, "CmdOrCtrl+Shift+D"),
            new(WorkbenchCommand.MERGE_SUBTITLE, "CmdOrCtrl+Shift+M"),
            new(WorkbenchCommand.EXPORT_VIDEO, "CmdOrCtrl+E"),
            new(WorkbenchCommand.EXIT, ""),
            new(WorkbenchCommand.VIEW_STYLES, ""),
            new(WorkbenchCommand.VIEW_EFFECTS, ""),
            new(WorkbenchCommand.VIEW_EXPORT, ""),
            new(WorkbenchCommand.VIEW_TIMELINE, ""),
            new(WorkbenchCommand.VIEW_PREVIEW, ""),
            new(WorkbenchCommand.VIEW_SUBTITLES, ""),
            new(WorkbenchCommand.LAYOUT_SAVE, ""),
            new(WorkbenchCommand.LAYOUT_SAVE_AS, ""),
            new(WorkbenchCommand.LAYOUT_MANAGE, ""),
            new(WorkbenchCommand.LAYOUT_RESTORE_DEFAULT, ""),
            new(WorkbenchCommand.VIEW_LOG, ""),
            new(WorkbenchCommand.IMPORT_ASS, ""),
            new(WorkbenchCommand.EXPORT_ASS, ""),
            new(WorkbenchCommand.OPEN_SUBTITLE_DETAILS, ""),
            new(WorkbenchCommand.END_TEXT_INPUT, "Escape")
        ];
    }
}
