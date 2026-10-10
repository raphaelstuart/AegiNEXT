using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsFormattingToggleUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormattingTogglesShareToolbarAppearanceAndTrackSelectionAndUndo(bool dark)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT
        });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        context.Session.SelectCue(id);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        var toolbarToggle = UiTestActions.Find<ToggleButton>(host, "KaraokeSnapToggle");
        Assert.True(toolbarToggle.IsChecked);
        Assert.Equal(0, UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex);

        foreach (var name in new[] { "Bold", "Italic", "Underline", "Strikethrough" })
        {
            var toggle = UiTestActions.Find<ToggleButton>(host, name + "SelectionButton");
            Assert.False(toggle.IsChecked);
            toolbarToggle.IsChecked = false;
            AssertSameAppearance(toolbarToggle, toggle);
            UiTestActions.Click(host, toggle.Name!);
            Assert.True(toggle.IsChecked);
            toolbarToggle.IsChecked = true;
            AssertSameAppearance(toolbarToggle, toggle);
            var line = context.Session.Editor.Snapshot.Subtitles.Single(value => value.Id == id);
            var selectedStyle = Assert.Single(line.InlineSpans).Style.ApplyTo(line.Style);
            Assert.True(IsActive(selectedStyle, name));
            Assert.False(IsActive(line.Style, name));

            rich.SetSelection(1, 2);
            Assert.False(toggle.IsChecked);
            rich.SetSelection(0, 1);
            Assert.True(toggle.IsChecked);
            Assert.True(context.Session.Editor.Undo());
            Assert.False(toggle.IsChecked);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.True(context.Session.Editor.Redo());
            Assert.True(toggle.IsChecked);
            UiTestActions.Click(host, toggle.Name!);
            Assert.False(toggle.IsChecked);
            toolbarToggle.IsChecked = false;
            AssertSameAppearance(toolbarToggle, toggle);
            Assert.True(context.Session.Editor.Undo());
            Assert.True(toggle.IsChecked);
            Assert.True(context.Session.Editor.Undo());
            Assert.False(toggle.IsChecked);
            Assert.False(context.Session.Editor.CanUndo);
        }
    }

    private static bool IsActive(SubtitleStyle style, string name) => name switch
    {
        "Bold" => style.Bold,
        "Italic" => style.Italic,
        "Underline" => style.Underline,
        _ => style.Strikethrough
    };

    private static void AssertSameAppearance(ToggleButton expected, ToggleButton actual)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(32, actual.Bounds.Width);
        Assert.Equal(32, actual.Bounds.Height);
        Assert.Equal(expected.CornerRadius, actual.CornerRadius);
        Assert.Equal(expected.Padding, actual.Padding);
        Assert.Equal(expected.BorderThickness, actual.BorderThickness);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected.Background).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual.Background).Color);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected.BorderBrush).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual.BorderBrush).Color);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected.Foreground).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual.Foreground).Color);
        Assert.Equal(expected.HorizontalContentAlignment, actual.HorizontalContentAlignment);
        Assert.Equal(expected.VerticalContentAlignment, actual.VerticalContentAlignment);
    }
}
