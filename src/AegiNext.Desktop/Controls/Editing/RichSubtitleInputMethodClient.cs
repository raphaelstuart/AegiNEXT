using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;

namespace AegiNext.Desktop.Controls;

internal sealed class RichSubtitleInputMethodClient(RichSubtitleEditor owner) : TextInputMethodClient
{
    public override Visual TextViewVisual => owner;
    public override bool SupportsPreedit => true;
    public override bool SupportsSurroundingText => true;
    public override string SurroundingText => owner.Text;
    public override Rect CursorRectangle => owner.CaretRectangle;
    public override TextSelection Selection
    {
        get => new(owner.SelectionStart, owner.SelectionEnd);
        set => owner.SetSelection(value.Start, value.End);
    }

    /// <inheritdoc />
    public override void SetPreeditText(string? text) => owner.SetPreedit(text, null);
    /// <inheritdoc />
    public override void SetPreeditText(string? text, int? cursorPos) => owner.SetPreedit(text, cursorPos);
    /// <inheritdoc />
    public override void ExecuteContextMenuAction(ContextMenuAction action)
    {
        switch (action)
        {
            case ContextMenuAction.Copy:
                _ = owner.ClipboardAsync(Key.C);
                break;
            case ContextMenuAction.Cut:
                _ = owner.ClipboardAsync(Key.X);
                break;
            case ContextMenuAction.Paste:
                _ = owner.ClipboardAsync(Key.V);
                break;
            case ContextMenuAction.SelectAll:
                owner.SetSelection(0, owner.Text.Length);
                break;
        }
    }

    internal void NotifySelection()
    {
        RaiseSelectionChanged();
        RaiseCursorRectangleChanged();
    }

    internal void NotifyContent()
    {
        RaiseSurroundingTextChanged();
        NotifySelection();
    }
}
