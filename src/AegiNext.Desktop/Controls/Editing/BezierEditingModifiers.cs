using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

internal static class BezierEditingModifiers
{
    internal static BezierEditAction GetAction(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            return BezierEditAction.DELETE;
        }
        return modifiers.HasFlag(KeyModifiers.Shift) && !modifiers.HasFlag(KeyModifiers.Alt)
            ? BezierEditAction.INSERT : BezierEditAction.NONE;
    }
}
