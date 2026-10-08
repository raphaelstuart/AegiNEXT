using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Startup;

internal sealed class WelcomeProjectSection(string titleKey) : WelcomeListItem
{
    public override bool IsProject => false;
    public string Title => Localization.Get(titleKey);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
    }
}
