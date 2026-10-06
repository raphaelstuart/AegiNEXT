using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleFontEditingUiTests
{
    [AvaloniaFact]
    public async Task FontSearchRemainsDraftUntilCommittedAndUndoRestoresTheStyle()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        var original = window.DocumentSnapshot;
        var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontCombo");
        Assert.Equal(Assert.Single(original.Subtitles).Style.FontFamily, picker.Text);
        picker.Text = "Custom Subtitle Face";
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
        Assert.True(picker.CommitText());
        Assert.Equal("Custom Subtitle Face", Assert.Single(window.DocumentSnapshot.Subtitles).Style.FontFamily);
        var committed = window.DocumentSnapshot;
        Assert.True(picker.CommitText());
        Assert.Same(committed, window.DocumentSnapshot);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Assert.Equal(Assert.Single(original.Subtitles).Style, Assert.Single(window.DocumentSnapshot.Subtitles).Style);
        Assert.Equal(Assert.Single(original.Subtitles).Style.FontFamily, picker.Text);
    }
}
