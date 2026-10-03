using Avalonia.Input;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>统一解析、规范化和校验应用快捷键，冲突按实际键与修饰键比较。</summary>
public static class ShortcutConfiguration
{
    private const KeyModifiers SUPPORTED_MODIFIERS = KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt | KeyModifiers.Shift;
    private static readonly HashSet<string> keyNames = new(Enum.GetNames<Key>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>拒绝重复命令、非法手势以及当前平台上的重复快捷键；空手势不占用键。</summary>
    public static void Validate(IEnumerable<ShortcutBinding?> bindings, bool? isMacOs = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var commands = new HashSet<WorkbenchCommand>();
        var gestures = new Dictionary<(Key Key, KeyModifiers Modifiers), WorkbenchCommand>();
        foreach (var binding in bindings)
        {
            if (binding is null || !Enum.IsDefined(binding.Command) || !commands.Add(binding.Command))
            {
                throw new InvalidDataException("快捷键包含未知或重复的命令。");
            }

            if (binding.Gesture is null)
            {
                throw new InvalidDataException("快捷键文本不能为空引用；禁用命令请使用空字符串。");
            }

            var gesture = Parse(binding.Gesture, isMacOs ?? OperatingSystem.IsMacOS());
            if (gesture is not null && !gestures.TryAdd((gesture.Key, gesture.KeyModifiers), binding.Command))
            {
                throw new InvalidDataException($"快捷键 {NormalizeGesture(binding.Gesture, isMacOs)} 已被其他命令使用。");
            }
        }
    }

    /// <summary>将有效文本规范化为可持久化手势；当前平台主修饰键写为 CmdOrCtrl。</summary>
    public static string NormalizeGesture(string gesture, bool? isMacOs = null)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var mac = isMacOs ?? OperatingSystem.IsMacOS();
        var parsed = Parse(gesture, mac);
        return parsed is null ? string.Empty : FormatGesture(parsed.Key, parsed.KeyModifiers, mac);
    }

    /// <summary>将按键捕获结果规范化；纯修饰键或未定义键明确拒绝。</summary>
    public static string FormatGesture(Key key, KeyModifiers modifiers, bool? isMacOs = null)
    {
        ValidateKey(key, modifiers);
        var primary = (isMacOs ?? OperatingSystem.IsMacOS()) ? KeyModifiers.Meta : KeyModifiers.Control;
        var parts = new List<string>();
        if ((modifiers & primary) != 0)
        {
            parts.Add("CmdOrCtrl");
            modifiers &= ~primary;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            parts.Add("Control");
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            parts.Add("Meta");
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        parts.Add(key == Key.Enter ? nameof(Key.Enter) : key.ToString());
        return string.Join('+', parts);
    }

    internal static KeyGesture? Parse(string text, bool isMacOs)
    {
        if (text.Length > 128 || text.Any(char.IsControl))
        {
            throw new InvalidDataException("快捷键文本包含控制字符或超过长度限制。");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var resolved = ResolveModifiers(text.Trim(), isMacOs);
            var gesture = KeyGesture.Parse(resolved);
            ValidateKey(gesture.Key, gesture.KeyModifiers);
            return gesture;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("快捷键格式无效。", error);
        }
    }

    private static string ResolveModifiers(string text, bool isMacOs)
    {
        if (text == "+")
        {
            text = nameof(Key.OemPlus);
        }
        else if (text.EndsWith("++", StringComparison.Ordinal))
        {
            text = text[..^1] + nameof(Key.OemPlus);
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (!keyNames.Contains(parts[^1]))
        {
            throw new InvalidDataException("快捷键必须包含已命名的有效按键。");
        }

        var modifiers = KeyModifiers.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index].ToUpperInvariant() switch
            {
                "CMDORCTRL" => isMacOs ? KeyModifiers.Meta : KeyModifiers.Control,
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "CMD" or "META" => KeyModifiers.Meta,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                _ => throw new InvalidDataException("快捷键包含未知修饰键。")
            };
            if ((modifiers & modifier) != 0)
            {
                throw new InvalidDataException("快捷键修饰键重复。");
            }

            modifiers |= modifier;
            parts[index] = modifier.ToString();
        }

        return string.Join('+', parts);
    }

    private static void ValidateKey(Key key, KeyModifiers modifiers)
    {
        if (!Enum.IsDefined(key) || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed ||
            (modifiers & ~SUPPORTED_MODIFIERS) != 0)
        {
            throw new InvalidDataException("快捷键必须包含有效的非修饰键。");
        }
    }
}
