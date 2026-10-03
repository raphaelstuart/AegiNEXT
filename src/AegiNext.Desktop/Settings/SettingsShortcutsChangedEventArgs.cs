using System.Collections.Immutable;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Settings;

/// <summary>已通过统一快捷键校验的完整绑定集合。</summary>
public sealed class SettingsShortcutsChangedEventArgs(ImmutableArray<ShortcutBinding> bindings) : EventArgs
{
    public ImmutableArray<ShortcutBinding> Bindings { get; } = bindings;
}
