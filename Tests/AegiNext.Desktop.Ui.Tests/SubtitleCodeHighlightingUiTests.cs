using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleCodeHighlightingUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodeUsesNativeColoredRunsAndThemeRefreshPreservesSourceSelectionAndUndo(bool dark)
    {
        await using var context = new MainWindowTestContext();
        Prepare(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        host.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        UiTestActions.Find<TabControl>(host, "SubtitleDetailsTabs").SelectedIndex = 1;
        Flush(host);
        var input = UiTestActions.Find<TextBox>(host, "SubtitleCodeInput");
        var snapshot = context.Session.Editor.Snapshot;
        var source = input.Text;
        Assert.False(input.IsUndoEnabled);
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<AssTextPresenter>());
        Assert.True(presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns)
            .Select(run => (run.Properties?.ForegroundBrush as ISolidColorBrush)?.Color).Distinct().Count() >= 2);
        Capture(host, $"subtitle-ass-code-{(dark ? "dark" : "light")}.png");
        input.Focus();
        input.SelectionStart = 1;
        input.SelectionEnd = 4;
        Flush(host);
        var caret = presenter.TextLayout.HitTestTextPosition(3);
        Assert.True(caret.Height > 0);
        Assert.True(caret.X >= 0);
        host.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
        Flush(host);
        Assert.Equal(source, input.Text);
        Assert.Equal(1, input.SelectionStart);
        Assert.Equal(4, input.SelectionEnd);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        presenter.PreeditText = "字幕123";
        Flush(host);
        Assert.Contains("字幕123", string.Concat(presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns)
            .Select(run => run.Text.ToString())), StringComparison.Ordinal);
        Assert.Equal(source, input.Text);
        presenter.PreeditText = null;
    }

    [AvaloniaFact]
    public async Task ColoredCodeRetainsEnterNewlineInvalidDraftAndProjectUndo()
    {
        await using var context = new MainWindowTestContext();
        Prepare(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        UiTestActions.Find<TabControl>(host, "SubtitleDetailsTabs").SelectedIndex = 1;
        Flush(host);
        var input = UiTestActions.Find<TextBox>(host, "SubtitleCodeInput");
        var snapshot = context.Session.Editor.Snapshot;
        Assert.True(input.Focus());
        input.CaretIndex = input.Text!.Length;
        UiTestActions.Press(host, Key.Enter);
        Flush(host);
        Assert.EndsWith("\n", input.Text, StringComparison.Ordinal);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        input.SelectAll();
        host.KeyTextInput("{\\an2\\b1}中文 ABC123");
        Flush(host);
        UiTestActions.Press(host, Key.Z, RawInputModifiers.Control);
        Flush(host);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        input.SelectAll();
        host.KeyTextInput("{\\fs");
        Flush(host);
        Assert.Equal("{\\fs", input.Text);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.Single(input.GetVisualDescendants().OfType<AssTextPresenter>());
    }

    [AvaloniaFact]
    public async Task ResizingColoredWrappedCodeKeepsAllTextAndNativeCaretHitTesting()
    {
        await using var context = new MainWindowTestContext();
        Prepare(context, string.Concat(Enumerable.Repeat("中文 ABC123 😀 ", 40)));
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        UiTestActions.Find<TabControl>(host, "SubtitleDetailsTabs").SelectedIndex = 1;
        Flush(host);
        var input = UiTestActions.Find<TextBox>(host, "SubtitleCodeInput");
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<AssTextPresenter>());
        var snapshot = context.Session.Editor.Snapshot;
        var source = input.Text;
        input.Width = 180;
        Flush(host);
        var narrowLines = presenter.TextLayout.TextLines.Count;
        input.Width = 550;
        Flush(host);
        Assert.True(presenter.TextLayout.TextLines.Count < narrowLines);
        var offset = source!.Length - 1;
        var caret = presenter.TextLayout.HitTestTextPosition(offset);
        Assert.Equal(offset, presenter.TextLayout.HitTestPoint(caret.TopLeft).TextPosition);
        Assert.Equal(source, input.Text);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void SharedPresenterPreservesSelectionImeDecorationsAndMaskedText()
    {
        var presenter = new AssTextPresenter
        {
            Text = "{\\fs48}中😀e\u0301", Foreground = Brushes.Black, SelectionForegroundBrush = Brushes.Magenta,
            ShowSelectionHighlight = true, SelectionStart = 0, SelectionEnd = 6, CaretIndex = 3
        };
        var window = new Window { Width = 500, Height = 200, Content = presenter };
        try
        {
            window.Show();
            Flush(window);
            var selected = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).First();
            Assert.Equal(Colors.Magenta, Assert.IsAssignableFrom<ISolidColorBrush>(selected.Properties!.ForegroundBrush).Color);
            presenter.PreeditText = "仮😀";
            Flush(window);
            var preeditRuns = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns)
                .Where(run => run.Properties?.TextDecorations?.Contains(TextDecorations.Underline[0]) == true).ToArray();
            Assert.NotEmpty(preeditRuns);
            Assert.All(preeditRuns, run => Assert.Equal(Colors.Black,
                Assert.IsAssignableFrom<ISolidColorBrush>(run.Properties!.ForegroundBrush).Color));
            Assert.DoesNotContain(presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns),
                run => (run.Properties?.ForegroundBrush as ISolidColorBrush)?.Color == Colors.Magenta);
            Assert.Equal("{\\fs48}中😀e\u0301", presenter.Text);
            presenter.PreeditText = null;
            presenter.PasswordChar = '*';
            Flush(window);
            Assert.Equal(new string('*', presenter.Text.Length), string.Concat(presenter.TextLayout.TextLines
                .SelectMany(line => line.TextRuns).Select(run => run.Text.ToString())));
        }
        finally
        {
            window.Close();
        }
    }

    private static void Prepare(MainWindowTestContext context, string text = "中文 ABC123")
    {
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), text);
        context.Session.Editor.Reset(context.Session.Editor.Snapshot);
        context.Session.SelectCue(id);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
