using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsAlignmentUiTests
{
    [AvaloniaTheory]
    [InlineData(300, false, "en-US")]
    [InlineData(300, true, "zh-CN")]
    [InlineData(950, false, "zh-CN")]
    [InlineData(950, true, "en-US")]
    public async Task AlignmentButtonsWorkWithoutASelectionAndStayMutuallyExclusive(double width, bool dark, string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT, Language = language
        });
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ABCDEF\nAB");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = width;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 0);
        AssertActive(host, SubtitleTextAlignment.CENTER);

        foreach (var alignment in new[] { SubtitleTextAlignment.LEFT, SubtitleTextAlignment.RIGHT, SubtitleTextAlignment.CENTER })
        {
            var name = ButtonName(alignment);
            var button = UiTestActions.Find<ToolbarToggleButton>(host, name);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal(32, button.Bounds.Width);
            Assert.Equal(32, button.Bounds.Height);
            Assert.IsType<MaterialIcon>(button.Content);
            UiTestActions.Click(host, name);
            AssertActive(host, alignment);
            Assert.Equal(alignment, context.Session.Editor.Snapshot.Subtitles[0].Style.TextAlign);
            Assert.Equal(original.Subtitles[0].Style.Alignment, context.Session.Editor.Snapshot.Subtitles[0].Style.Alignment);
            Assert.Equal(original.Subtitles[0].Style.Position, context.Session.Editor.Snapshot.Subtitles[0].Style.Position);
            Assert.Equal(0, rich.SelectionStart);
            Assert.Equal(0, rich.SelectionEnd);
            Assert.Null(rich.RenderDiagnostic);
            var changed = context.Session.Editor.Snapshot;
            UiTestActions.Click(host, name);
            Assert.Same(changed, context.Session.Editor.Snapshot);
            Capture(host, $"details-align-{alignment}-{width}-{(dark ? "dark" : "light")}-{language}.png");
        }

        Assert.True(context.Session.Editor.Undo());
        AssertActive(host, SubtitleTextAlignment.RIGHT);
        Assert.True(context.Session.Editor.Redo());
        AssertActive(host, SubtitleTextAlignment.CENTER);
        var beforeLanguageChange = context.Session.Editor.Snapshot;
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language == "en-US" ? "zh-CN" : "en-US" });
        Dispatcher.UIThread.RunJobs();
        foreach (var alignment in Enum.GetValues<SubtitleTextAlignment>())
        {
            var button = UiTestActions.Find<ToolbarToggleButton>(host, ButtonName(alignment));
            var key = "Workbench." + ButtonName(alignment).Replace("Button", "", StringComparison.Ordinal);
            Assert.Equal(Localization.Get(key), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get(key), AutomationProperties.GetName(button));
        }
        Assert.Same(beforeLanguageChange, context.Session.Editor.Snapshot);
        AssertActive(host, SubtitleTextAlignment.CENTER);
    }

    [AvaloniaFact]
    public async Task AlignmentPreservesTextSelectionAndTracksCueChangesAndHighlightMode()
    {
        await using var context = new MainWindowTestContext();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "ABCDEF\nAB");
        var second = context.Session.Editor.AddSubtitle(new(5), new(8), "Second\nLine");
        context.Session.Editor.UpdateSubtitle(second, line => line with { Style = line.Style with { TextAlign = SubtitleTextAlignment.RIGHT } });
        context.Session.SelectCue(first);
        context.Session.Details.SetKaraokeEnabled(true);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.Focus();
        rich.SetSelection(1, 3);
        UiTestActions.Click(host, "AlignLeftButton");
        Assert.Equal((1, 3), (rich.SelectionStart, rich.SelectionEnd));
        Assert.Equal(original.Subtitles[0].InlineSpans, context.Session.Editor.Snapshot.Subtitles[0].InlineSpans);
        Assert.Equal(original.Subtitles[0].Karaoke, context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        AssertActive(host, SubtitleTextAlignment.LEFT);
        UiTestActions.Find<ToggleButton>(host, "HighlightStyleToggle").IsChecked = true;
        foreach (var alignment in Enum.GetValues<SubtitleTextAlignment>())
        {
            Assert.False(UiTestActions.Find<ToolbarToggleButton>(host, ButtonName(alignment)).IsEffectivelyEnabled);
        }
        UiTestActions.Find<ToggleButton>(host, "HighlightStyleToggle").IsChecked = false;
        context.Session.SelectCue(second);
        AssertActive(host, SubtitleTextAlignment.RIGHT);
        context.Session.SelectCue(first);
        AssertActive(host, SubtitleTextAlignment.LEFT);
        Assert.True(context.Session.Editor.Undo());
        AssertActive(host, SubtitleTextAlignment.CENTER);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task AlignmentButtonsAreDisabledWithoutATarget()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "AB");
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        context.Session.Editor.RemoveSubtitle(id);
        Assert.Null(context.Session.Details.Line);
        foreach (var alignment in Enum.GetValues<SubtitleTextAlignment>())
        {
            var button = UiTestActions.Find<ToolbarToggleButton>(host, ButtonName(alignment));
            Assert.False(button.IsEffectivelyEnabled);
            Assert.False(button.IsChecked);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickingAlignmentFromAFocusedStyleFieldUsesOneAtomicTransaction(bool invalid)
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "AB\nC");
        context.Session.SelectCue(id);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        context.Session.Details.EditText(1, 1, "Z");
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        text.Focus();
        UiTestActions.SetText(text, invalid ? "invalid" : "88");
        Assert.Same(original, context.Session.Editor.Snapshot);

        UiTestActions.Click(host, "AlignLeftButton");
        if (invalid)
        {
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal("invalid", text.Text);
            Assert.Equal("AZ\nC", context.Session.Details.Line!.Text);
            AssertActive(host, SubtitleTextAlignment.CENTER);
            Assert.False(context.Session.Editor.CanUndo);
        }
        else
        {
            var changed = context.Session.Editor.Snapshot.Subtitles[0];
            Assert.Equal("AZ\nC", changed.Text);
            Assert.Equal(88, Assert.Single(changed.InlineSpans).Style.FontSize);
            Assert.Equal(SubtitleTextAlignment.LEFT, changed.Style.TextAlign);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
    }

    [AvaloniaTheory]
    [InlineData(SubtitleTextAlignment.LEFT, false)]
    [InlineData(SubtitleTextAlignment.CENTER, false)]
    [InlineData(SubtitleTextAlignment.LEFT, true)]
    [InlineData(SubtitleTextAlignment.CENTER, true)]
    public async Task ClickingAlignmentFromAFocusedDurationPopupUsesOneUndo(SubtitleTextAlignment alignment, bool invalid)
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "AB");
        context.Session.SelectCue(id);
        context.Session.Details.SetKaraokeEnabled(true);
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        var clip = original.Subtitles[0].Karaoke[0];
        Assert.True(context.Session.Details.SelectClip(clip.Id));
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        var alignmentButton = UiTestActions.Find<ToolbarToggleButton>(host, ButtonName(alignment));
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        popup.ShowAt(axis);
        var input = UiTestActions.Find<NumericDraftInput>(Assert.IsAssignableFrom<Control>(popup.Child), "KaraokeDurationInput");
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        text.Focus();
        UiTestActions.SetText(text, invalid ? "invalid" : "1.5");
        Assert.Equal(invalid ? "invalid" : "1.5", context.Session.Details.DurationText);
        Assert.Same(original, context.Session.Editor.Snapshot);

        var point = alignmentButton.TranslatePoint(new Point(16, 16), host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        Assert.Same(original, context.Session.Editor.Snapshot);
        host.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        if (invalid)
        {
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.True(popup.IsOpen);
            Assert.Equal("invalid", text.Text);
            AssertActive(host, SubtitleTextAlignment.CENTER);
            Assert.False(context.Session.Editor.CanUndo);
            return;
        }
        var changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(alignment == SubtitleTextAlignment.CENTER ? (SubtitleTextAlignment?)null : alignment, changed.Style.TextAlign);
        Assert.Equal(new MediaTime(3, 2), changed.Karaoke[0].End - changed.Karaoke[0].Start);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void AssertActive(Window host, SubtitleTextAlignment alignment)
    {
        foreach (var value in Enum.GetValues<SubtitleTextAlignment>())
        {
            Assert.Equal(value == alignment, UiTestActions.Find<ToolbarToggleButton>(host, ButtonName(value)).IsChecked);
        }
    }

    private static string ButtonName(SubtitleTextAlignment alignment) => alignment switch
    {
        SubtitleTextAlignment.LEFT => "AlignLeftButton",
        SubtitleTextAlignment.CENTER => "AlignCenterButton",
        SubtitleTextAlignment.RIGHT => "AlignRightButton",
        _ => throw new ArgumentOutOfRangeException(nameof(alignment))
    };

    private static void Capture(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
    }
}
