using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleFontEditingUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerSelectionFromTheFullFontMenuChangesTheStyleWithOneUndo(bool details)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Fonts.EnsureLoadedAsync();
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var window = context.Window;
        var cueId = Assert.Single(window.DocumentSnapshot.Subtitles).Id;
        context.Session.Editor.UpdateSubtitle(cueId, line => line with { Text = "ab" });
        var original = window.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(Assert.Single(original.Subtitles).Id);
        if (details)
        {
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        }
        var host = details ? Assert.Single(window.Layouts.FloatingWindows) : window;
        if (details)
        {
            host.Width = 950;
            host.Height = 1200;
            UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        }
        var picker = UiTestActions.Find<FontFamilyPicker>(host, details ? "SelectionFontInput" : "FontCombo");
        var selectedFont = context.Session.Fonts.Candidates.First(candidate =>
            candidate.Selection.Variant is { Weight: >= 700 } &&
            candidate.Selection.FamilyName != picker.CurrentFont.FamilyName).Selection;
        var cancelledGestures = 0;
        context.ViewModel.GesturesCancelled += (_, _) => cancelledGestures++;
        picker.OpenFontList();
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsAssignableFrom<MenuBase>(Assert.Single(picker.GetVisualDescendants().OfType<Popup>()).Child);
        var family = menu.Items.Cast<MenuItem>().Single(item => Equals(item.Header, selectedFont.FamilyName));
        UiTestActions.ClickFontMenuItem(family);
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, cancelledGestures);
        if (family.HasSubMenu)
        {
            Assert.Same(original, window.DocumentSnapshot);
            Assert.True(family.IsSubMenuOpen);
            var variant = family.Items.Cast<MenuItem>().Single(item => Equals(item.Header, selectedFont.Variant?.Name));
            Assert.True(variant.IsAttachedToVisualTree());
            UiTestActions.ClickFontMenuItem(variant);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, cancelledGestures);
        var cue = Assert.Single(window.DocumentSnapshot.Subtitles);
        var style = details ? Assert.Single(cue.InlineSpans).Style.ApplyTo(cue.Style) : cue.Style;
        if (details)
        {
            Assert.Equal(original.Subtitles[0].Style, cue.Style);
        }
        Assert.Equal(selectedFont.FamilyName, style.FontFamily);
        Assert.Equal(selectedFont.Variant, style.FontVariant);
        Assert.Equal(selectedFont.DisplayName, picker.Text);
        Assert.False(picker.IsDropDownOpen);
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task NavigatingTheFontSubmenuKeepsTheStyleDraftUntilOneVariantIsCommitted()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Fonts.EnsureLoadedAsync();
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var window = context.Window;
        var original = window.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(Assert.Single(original.Subtitles).Id);
        var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontCombo");
        var selectedFont = context.Session.Fonts.Candidates.First(candidate => candidate.Selection.Variant is { Weight: >= 700 }).Selection;
        var input = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput(selectedFont.DisplayName);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
        Assert.True(picker.IsDropDownOpen);
        UiTestActions.Press(window, Key.Down);
        UiTestActions.Press(window, Key.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.IsDropDownOpen);
        Assert.Same(original, window.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        UiTestActions.Press(window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(selectedFont.Variant, Assert.Single(window.DocumentSnapshot.Subtitles).Style.FontVariant);
        Assert.False(picker.IsDropDownOpen);
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task FontSearchRemainsDraftUntilCommittedAndUndoRestoresTheStyle()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
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
