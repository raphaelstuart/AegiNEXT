using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Windowing;

internal interface IWorkbenchFocusCommandTarget
{
    bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement);
    bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement);
}
