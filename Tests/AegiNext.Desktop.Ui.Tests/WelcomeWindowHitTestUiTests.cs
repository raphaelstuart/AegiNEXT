using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WelcomeWindowHitTestUiTests
{
    [AvaloniaTheory]
    [InlineData("icon-gap")]
    [InlineData("right")]
    [InlineData("left-padding")]
    [InlineData("bottom-padding")]
    public async Task ClickingBlankSpaceInsideARecentProjectRowOpensThatProject(string region)
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var first = Path.Combine(environment.DirectoryPath, "中文 Project 123.aeginext");
        var second = Path.Combine(environment.DirectoryPath, "Other Project.aeginext");
        await File.WriteAllTextAsync(first, "first fixture", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "second fixture", TestContext.Current.CancellationToken);
        await history.RecordAsync(first);
        await history.RecordAsync(second);
        var opened = new List<string?>();
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            path =>
            {
                opened.Add(path);
                return Task.CompletedTask;
            }, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(second, viewModel.SelectedProject!.Path);
            var item = Assert.Single(viewModel.Projects, project => project.Path == first);
            var list = UiTestActions.Find<ListBox>(window, "RecentProjectList");
            var container = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
                candidate => ReferenceEquals(candidate.DataContext, item));
            var entry = Assert.Single(container.GetVisualDescendants().OfType<Grid>(),
                candidate => candidate.Classes.Contains("project-entry"));
            var localPoint = region switch
            {
                "icon-gap" => new Point(container.Padding.Left + 46,
                    container.Padding.Top + entry.Bounds.Height / 2),
                "right" => new(container.Bounds.Width - container.Padding.Right - 1,
                    container.Padding.Top + entry.Bounds.Height / 2),
                "left-padding" => new(container.Padding.Left / 2, container.Bounds.Height / 2),
                "bottom-padding" => new(container.Bounds.Width / 2,
                    container.Bounds.Height - container.Padding.Bottom / 2),
                _ => throw new ArgumentOutOfRangeException(nameof(region))
            };
            Assert.True(container.Bounds.Width > 0 && container.Bounds.Height > 0);
            var point = container.TranslatePoint(localPoint, window);
            Assert.NotNull(point);

            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(first, Assert.Single(opened));
            Assert.Same(item, viewModel.SelectedProject);
        }
        finally
        {
            window.Close();
        }
    }
}
