using System.Windows.Input;

namespace AegiNext.Desktop.Menus;

internal sealed class WorkbenchCommandAdapter(Action execute, Func<bool> canExecute) : ICommand
{
    private bool? previousAvailability;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return canExecute();
    }

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    internal void RefreshAvailability()
    {
        var available = canExecute();
        if (previousAvailability == available)
        {
            return;
        }

        previousAvailability = available;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
