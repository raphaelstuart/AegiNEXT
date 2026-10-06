using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private MaskCanvasCursors? maskCursors;
    private TopLevel? maskCursorHost;
    private KeyModifiers maskCursorModifiers;

    private void UpdateMaskCursor()
    {
        if (!IsMaskMode || disposed)
        {
            Cursor = null;
            return;
        }
        maskCursors ??= new();
        var bezier = EditMode is CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR;
        Cursor = bezier && maskCursorModifiers.HasFlag(KeyModifiers.Control) ? maskCursors.Delete :
            bezier && maskCursorModifiers.HasFlag(KeyModifiers.Shift) && !maskCursorModifiers.HasFlag(KeyModifiers.Alt) ? maskCursors.Insert : maskCursors.Cross;
    }

    private void ObserveMaskModifiers(KeyEventArgs e, bool pressed)
    {
        var modifier = e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            _ => KeyModifiers.None
        };
        maskCursorModifiers = pressed ? e.KeyModifiers | modifier : e.KeyModifiers & ~modifier;
        UpdateMaskCursor();
    }

    private void OnMaskModifierPressed(object? sender, KeyEventArgs e) => ObserveMaskModifiers(e, true);
    private void OnMaskModifierReleased(object? sender, KeyEventArgs e) => ObserveMaskModifiers(e, false);

    private void DetachMaskCursorHost()
    {
        maskCursorHost?.RemoveHandler(KeyDownEvent, OnMaskModifierPressed);
        maskCursorHost?.RemoveHandler(KeyUpEvent, OnMaskModifierReleased);
        maskCursorHost = null;
        maskCursorModifiers = KeyModifiers.None;
        UpdateMaskCursor();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (disposed)
        {
            return;
        }
        maskCursorHost = TopLevel.GetTopLevel(this);
        maskCursorHost?.AddHandler(KeyDownEvent, OnMaskModifierPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        maskCursorHost?.AddHandler(KeyUpEvent, OnMaskModifierReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        UpdateMaskCursor();
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        maskCursorModifiers = e.KeyModifiers;
        UpdateMaskCursor();
    }
}
