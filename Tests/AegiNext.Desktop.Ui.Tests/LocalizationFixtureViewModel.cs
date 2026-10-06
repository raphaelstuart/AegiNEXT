using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>Provides mutable business state independent of the active language.</summary>
public sealed class LocalizationFixtureViewModel : ObservableObject
{
    private string businessText = "User subtitle draft";

    public IReadOnlyList<string> Items { get; } = ["A", "B"];

    public string BusinessText
    {
        get => businessText;
        set => SetProperty(ref businessText, value);
    }
}
