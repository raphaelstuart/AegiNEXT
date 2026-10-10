using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Panels.SubtitleDetails;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class BusinessNumericDraggingUiTests
{
    [AvaloniaFact]
    public async Task StyleTitleDragUpdatesThePreviewDraftAndCreatesExactlyOneUndoOnRelease()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Subtitle");
        context.Session.SelectCue(id);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            input.FocusInput();
            var end = BeginDrag(host, input);
            Assert.True(input.IsTitleDragging);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(original.Subtitles[0].Style.FontSize + 12, context.Session.SelectedCue!.Style.FontSize);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapeOrGlobalDraftCommitCancelsAnUnreleasedStyleDrag(bool globalCommit)
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "Subtitle");
        context.Session.SelectCue(id);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            var text = input.RawText;
            var end = BeginDrag(host, input);
            if (globalCommit)
            {
                Assert.True(context.Session.TryCommitDrafts());
            }
            else
            {
                UiTestActions.Press(host, Key.Escape);
            }
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(input.IsTitleDragging);
            Assert.Equal(text, input.RawText);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ReplacingTheSelectedSubtitleCancelsTheDragBeforeCommittingEitherSubtitle()
    {
        await using var context = new MainWindowTestContext();
        var first = context.Session.Editor.AddSubtitle(new(0), new(4), "First");
        var second = context.Session.Editor.AddSubtitle(new(4), new(8), "Second");
        context.Session.SelectCue(first);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        var view = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var host = new Window { Width = 650, Height = 920, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            var end = BeginDrag(host, input);
            context.Session.SelectCue(second);
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(second, context.Session.SelectedCueId);
            Assert.False(input.IsTitleDragging);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task SubtitleSelectionTitleDragCommitsOnlyTheSelectedRangeAndUndoesOnce()
    {
        await using var context = new MainWindowTestContext();
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ABC");
        context.Session.SelectCue(id);
        var original = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(original);
        var view = new SubtitleDetailsPanelView(context.Session);
        var host = new Window { Width = 950, Height = 920, Content = view };
        try
        {
            host.Show();
            UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(1, 2);
            var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
            var end = BeginDrag(host, input);
            Assert.True(input.IsTitleDragging);
            Assert.Same(original, context.Session.DocumentSnapshot);
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var span = Assert.Single(context.Session.SelectedCue!.InlineSpans);
            Assert.Equal(1, span.Utf16Start);
            Assert.Equal(1, span.Utf16Length);
            Assert.Equal(original.Subtitles[0].Style.FontSize + 12, span.Style.FontSize);
            Assert.Equal(original.Subtitles[0].Style.FontSize, context.Session.SelectedCue.Style.FontSize);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            host.Close();
            view.Dispose();
        }
    }

    private static Point BeginDrag(Window host, NumericDraftInput input)
    {
        var title = Assert.Single(host.GetVisualDescendants().OfType<NumericDragLabel>(), label => label.Input == input);
        title.BringIntoView();
        host.UpdateLayout();
        var start = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
        var end = start + new Vector(12, 0);
        host.MouseDown(start, MouseButton.Left);
        host.MouseMove(end);
        Dispatcher.UIThread.RunJobs();
        return end;
    }
}
