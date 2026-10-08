using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Startup;

internal abstract class WelcomeListItem : ObservableObject
{
    public abstract bool IsProject { get; }
}
