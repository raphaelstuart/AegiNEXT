using Avalonia.Input;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>应用内精确快捷键路由，保留文本输入控件的编辑按键。</summary>
public sealed class ShortcutRouter
{
    private readonly Dictionary<(Key Key, KeyModifiers Modifiers), WorkbenchCommand> commands = [];

    /// <summary>验证并复制绑定；此后修改设置不会改变现有路由实例。</summary>
    public ShortcutRouter(IEnumerable<ShortcutBinding> bindings, bool? isMacOs = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var copy = bindings.ToArray();
        ShortcutConfiguration.Validate(copy, isMacOs);
        foreach (var binding in copy)
        {
            if (ShortcutConfiguration.Parse(binding.Gesture, isMacOs ?? OperatingSystem.IsMacOS()) is { } gesture)
            {
                commands.Add((gesture.Key, gesture.KeyModifiers), binding.Command);
            }
        }
    }

    /// <summary>匹配完整键与修饰键；返回 false 时调用者不应标记事件已处理。</summary>
    public bool TryResolve(Key key, KeyModifiers modifiers, bool isTextInput, out WorkbenchCommand command)
    {
        if (!commands.TryGetValue((key, modifiers), out command))
        {
            return false;
        }
        return command is WorkbenchCommand.END_TEXT_INPUT or WorkbenchCommand.ADVANCE_SUBTITLE_ROW or
                   WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK || !isTextInput || !ProtectsTextInput(key, modifiers);
    }

    private static bool ProtectsTextInput(Key key, KeyModifiers modifiers)
    {
        if (key is >= Key.F1 and <= Key.F24)
        {
            return false;
        }

        if (key is Key.Space or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or
            Key.Back or Key.Delete or Key.Insert or Key.Tab or Key.Enter or Key.Escape)
        {
            return true;
        }

        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 ||
            (modifiers & KeyModifiers.Alt) != 0 && (modifiers & KeyModifiers.Meta) == 0)
        {
            return true;
        }

        return key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y;
    }
}
