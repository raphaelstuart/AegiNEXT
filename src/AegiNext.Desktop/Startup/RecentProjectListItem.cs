using AegiNext.Desktop.I18n;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectListItem(RecentProjectEntry entry) : ObservableObject
{
    private readonly RecentProjectIcon icon = new(entry.Name, entry.Path);

    internal RecentProjectEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public string IconInitials => icon.Initials;
    public IBrush IconBackground => IsUnavailable ? Brushes.DimGray : icon.Background;
    public IBrush IconForeground => IsUnavailable ? Brushes.Gainsboro : icon.Foreground;
    public bool IsUnavailable => !File.Exists(Path);
    public string Availability => IsUnavailable ? Localization.Get("Welcome.ProjectUnavailable") : string.Empty;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(IconBackground));
        OnPropertyChanged(nameof(IconForeground));
        OnPropertyChanged(nameof(Availability));
    }
}
