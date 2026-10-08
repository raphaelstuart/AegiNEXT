using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TaskSettingsUiTests
{
    [AvaloniaFact]
    public void TaskLimitUsesKeyboardInputAndPreservesInvalidDraftAcrossNavigationAndLanguage()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.TASKS);
            var input = UiTestActions.Find<NumericDraftInput>(window, "MaximumConcurrentTasksInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var changes = new List<int>();
            window.TasksChanged += (_, args) => changes.Add(args.MaximumConcurrentTasks);
            Assert.True(box.Focus());
            box.SelectAll();
            window.KeyTextInput("33");
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            Assert.Empty(changes);

            window.SelectPage(SettingsPage.MEDIA);
            Localization.SetLanguage("zh-CN");
            window.UpdatePreferences(new() { MaximumConcurrentTasks = 8 });
            window.SelectPage(SettingsPage.TASKS);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("任务", window.ViewModel.PageTitle);
            Assert.Equal("33", input.RawText);
            Assert.True(window.ViewModel.HasError);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("8", input.RawText);
            Assert.False(window.ViewModel.HasError);
            box.SelectAll();
            window.KeyTextInput("1");
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(1, Assert.Single(changes));
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }
}
