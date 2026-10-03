using System.Windows.Input;

namespace AegiNext.Desktop.Menus;

internal sealed record LayoutMenuChoice(string Id, string Title, bool IsSelected, ICommand Command);
