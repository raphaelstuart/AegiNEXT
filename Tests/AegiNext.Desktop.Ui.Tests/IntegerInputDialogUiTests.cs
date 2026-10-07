using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class IntegerInputDialogUiTests
{
    [AvaloniaTheory]
    [InlineData("0", 0)]
    [InlineData("+125", 125)]
    [InlineData("-125", -125)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public async Task ConfirmationReturnsSignedIntegerAndClosesTheOwnedDialog(string text, int expected)
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            Assert.Single(dialog.GetLogicalDescendants().OfType<WindowTitleBar>());
            var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
            Assert.Equal("0", input.RawText);
            var editor = input.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Same(editor, dialog.FocusManager!.GetFocusedElement());
            UiTestActions.SetText(editor, text);

            UiTestActions.Click(dialog, "ConfirmButton");

            Assert.Equal(expected, await answer);
            Assert.False(dialog.IsVisible);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    public async Task InvalidRawTextSurvivesFocusLossAndConfirmationThenCanBeCorrected(string text)
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
            var editor = input.GetVisualDescendants().OfType<TextBox>().Single();
            UiTestActions.SetText(editor, text);

            UiTestActions.Click(dialog, "ConfirmButton");

            Assert.False(answer.IsCompleted);
            Assert.True(dialog.IsVisible);
            Assert.Equal(text, input.RawText);
            Assert.Equal(text, editor.Text);
            var error = UiTestActions.Find<TextBlock>(dialog, "IntegerInputError");
            Assert.True(error.IsVisible);
            Assert.Equal(Localization.Format("Workbench.IntegerInputInvalid", int.MinValue, int.MaxValue), error.Text);
            editor.Focus();
            UiTestActions.SetText(editor, "-25");
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.Equal(-25, await answer);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RequestedRangeRejectsOutOfBoundsWithoutClampingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint", 3, -10, 10));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
            var editor = input.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal("3", editor.Text);
            UiTestActions.SetText(editor, "11");
            UiTestActions.Click(dialog, "ConfirmButton");

            Assert.False(answer.IsCompleted);
            Assert.Equal("11", input.RawText);
            Assert.Equal("11", editor.Text);
            Assert.Equal(Localization.Format("Workbench.IntegerInputInvalid", -10, 10), UiTestActions.Find<TextBlock>(dialog, "IntegerInputError").Text);
            UiTestActions.SetText(editor, "-10");
            UiTestActions.Click(dialog, "ConfirmButton");
            Assert.Equal(-10, await answer);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExternalCancellationClosesTheDialogAndReleasesLanguageSubscription()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var cancellation = new CancellationTokenSource();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"), cancellation.Token);
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        var model = Assert.IsType<IntegerInputDialogViewModel>(dialog.DataContext);
        try
        {
            model.RawText = "invalid";
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.Move"), dialog.Title);
            Assert.Equal("invalid", UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput").RawText);
            Assert.Equal(Localization.Get("Workbench.Confirm"), UiTestActions.Find<Button>(dialog, "ConfirmButton").Content);
            var notifications = 0;
            model.PropertyChanged += (_, _) => notifications++;

            await Task.Run(cancellation.Cancel);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(await answer);
            Assert.False(dialog.IsVisible);
            Assert.Empty(owner.OwnedWindows);
            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, notifications);
            Assert.False(model.ConfirmCommand.CanExecute(null));
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task PreCancelledRequestDoesNotCreateAnOwnedWindow()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ShowIntegerInputAsync(
                new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"), new(true)));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnterDuringImeCompositionDoesNotConfirmTheDialog()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowIntegerInputAsync(new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint"));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
            var editor = input.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Focus();
            var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
            presenter.PreeditText = "1";
            UiTestActions.Press(dialog, Key.Enter);
            Assert.False(answer.IsCompleted);
            Assert.True(dialog.IsVisible);
            presenter.PreeditText = null;
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(0, await answer);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }
}
