using System.Runtime.CompilerServices;
using AegiNext.Application;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NewProjectDialogUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", 560)]
    [InlineData("en-US", 900)]
    [InlineData("zh-CN", 560)]
    [InlineData("zh-CN", 900)]
    public void LongMixedProjectPathsKeepFieldsAndActionsWithinTheDialog(string language, double width)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var owner = new Window();
        var workspace = Path.Combine(environment.DirectoryPath, "字幕项目 ABC123", "Workspace resources ABC123");
        using var model = new NewProjectDialogViewModel(workspace,
            (_, _) => Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED)),
            _ => Task.FromResult<string?>(null))
        {
            ProjectName = "混合项目 ABC 123"
        };
        var dialog = new NewProjectDialog(model)
        {
            Width = width,
            RequestedThemeVariant = language == "zh-CN" ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            owner.Show();
            dialog.Show(owner);
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            Assert.True(width >= dialog.MinWidth);
            Assert.InRange(dialog.ClientSize.Width, width - 1, width + 1);
            var name = UiTestActions.Find<TextBox>(dialog, "ProjectNameInput");
            var location = UiTestActions.Find<TextBox>(dialog, "ProjectLocationInput");
            var preview = UiTestActions.Find<TextBlock>(dialog, "ProjectPathPreview");
            var browse = UiTestActions.Find<Button>(dialog, "BrowseProjectLocationButton");
            var create = UiTestActions.Find<Button>(dialog, "CreateProjectButton");
            var cancel = UiTestActions.Find<Button>(dialog, "CancelButton");
            var body = Assert.IsType<Grid>(name.GetVisualParent());
            var nameLabel = body.Children.OfType<TextBlock>().Single(control => Grid.GetRow(control) == 0);
            var locationLabel = body.Children.OfType<TextBlock>().Single(control => Grid.GetRow(control) == 1);
            Control[] controls = [nameLabel, name, locationLabel, location, browse, preview, create, cancel];
            var bounds = controls.Select(control => BoundsIn(control, body)).ToArray();
            for (var index = 0; index < controls.Length; index++)
            {
                var rectangle = bounds[index];
                Assert.True(controls[index].IsEffectivelyVisible);
                Assert.True(double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y) &&
                    double.IsFinite(rectangle.Width) && double.IsFinite(rectangle.Height));
                Assert.True(rectangle.Width > 0 && rectangle.Height > 0);
                Assert.True(new Rect(body.Bounds.Size).Contains(rectangle),
                    $"Control {controls[index].Name ?? controls[index].GetType().Name} escaped the dialog body: {rectangle}.");
                for (var previous = 0; previous < index; previous++)
                {
                    Assert.False(rectangle.Intersects(bounds[previous]),
                        $"Dialog controls overlap: {controls[index].Name} and {controls[previous].Name}.");
                }
            }
            Assert.Equal(Localization.Get("Workbench.ProjectName"), nameLabel.Text);
            Assert.Equal(Localization.Get("Workbench.ProjectLocation"), locationLabel.Text);
            Assert.True(nameLabel.TextLayout.Width <= nameLabel.Bounds.Width + 1);
            Assert.True(locationLabel.TextLayout.Width <= locationLabel.Bounds.Width + 1);
            Assert.Equal(model.ProjectName, name.Text);
            Assert.Equal(workspace, location.Text);
            Assert.Contains(ProjectCreationService.GetProjectPath(new(model.ProjectName, workspace)), preview.Text);
            Assert.Equal(TextWrapping.Wrap, preview.TextWrapping);
            var renderedPathText = string.Concat(preview.TextLayout.TextLines
                .SelectMany(line => line.TextRuns).Select(run => run.Text.ToString()));
            var filename = model.ProjectName + ".aeginext";
            var expectedText = preview.Text!;
            var visibleSuffix = expectedText[expectedText.LastIndexOf(filename, StringComparison.Ordinal)..];
            Assert.EndsWith(visibleSuffix, renderedPathText, StringComparison.Ordinal);
            Assert.True(preview.TextLayout.Width <= preview.Bounds.Width + 1);
            Assert.True(preview.TextLayout.Height <= preview.Bounds.Height + 1,
                $"The final path was clipped at {language}/{width}: layout={preview.TextLayout.Height}, available={preview.Bounds.Height}.");
            Assert.InRange(new Rect(dialog.ClientSize).Bottom - BoundsIn(create, dialog).Bottom, 23, 25);
            if (width == dialog.MinWidth)
            {
                CaptureProjectWindow(dialog, $"new-project-{language}.png");
                if (language == "zh-CN")
                {
                    CaptureProjectsSettings(owner, workspace);
                }
            }
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ImeCandidateConfirmationDoesNotCreateAndHeldEnterCreatesOnce()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var attempts = 0;
        using var model = new NewProjectDialogViewModel(environment.DirectoryPath, (_, _) =>
        {
            attempts++;
            return Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.FAILED, new IOException("Retry later")));
        }, _ => Task.FromResult<string?>(null));
        var dialog = new NewProjectDialog(model);
        owner.Show();
        dialog.Show(owner);
        try
        {
            var input = UiTestActions.Find<TextBox>(dialog, "ProjectNameInput");
            Assert.True(input.Focus());
            var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
            presenter.PreeditText = "项目候选";
            UiTestActions.Press(dialog, Key.Enter);
            UiTestActions.Press(dialog, Key.Escape);
            Assert.Equal(0, attempts);
            Assert.True(dialog.IsVisible);
            presenter.PreeditText = null;
            input.SelectAll();
            dialog.KeyTextInput("项目 ABC 123");
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await model.Completion;
            Assert.Equal(1, attempts);
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(1, attempts);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            UiTestActions.Press(dialog, Key.Enter);
            await model.Completion;
            Assert.Equal(2, attempts);
            Assert.Equal("项目 ABC 123", model.ProjectName);
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void ClosedStandaloneDialogIsCollectibleAfterLanguageChanges()
    {
        using var environment = new UiTestEnvironment();
        var ownerButton = new Button { Content = "Owner focus" };
        var owner = new Window { Content = ownerButton };
        try
        {
            owner.Show();
            var weak = CreateClosedDialog(owner, environment.DirectoryPath);
            Assert.Empty(owner.OwnedWindows);
            var sentinelButton = new Button { Content = "Focus sentinel" };
            var sentinel = new Window { Content = sentinelButton };
            try
            {
                sentinel.Show(owner);
                sentinelButton.Focus();
            }
            finally
            {
                sentinel.Close();
            }
            owner.Activate();
            Assert.True(ownerButton.Focus());
            owner.Close();
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(weak.TryGetTarget(out _), "A closed project dialog must release localization and chrome subscriptions.");
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RegisteredDialogReusesItsTitleBarAndUnregistersWhenClosed()
    {
        await using var context = new MainWindowTestContext();
        var service = new WindowWorkbenchDialogService(context.Window,
            registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var workspace = Path.Combine(Path.GetTempPath(), "AegiNext.Ui.Tests", Guid.NewGuid().ToString("N"));
        var answer = service.ShowNewProjectAsync(workspace,
            (_, _) => Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED)));
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<NewProjectDialog>());
        try
        {
            Assert.Contains(dialog, context.WindowRegistry.Windows);
            var titleBar = Assert.Single(dialog.GetLogicalDescendants().OfType<WindowTitleBar>());
            Assert.Equal("NewProjectTitleBar", titleBar.Name);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.NewProject"), dialog.Title);
            Assert.Equal(dialog.Title, titleBar.Title);
            UiTestActions.Click(dialog, "CancelButton");

            Assert.False(await answer);
            Assert.DoesNotContain(dialog, context.WindowRegistry.Windows);
            Assert.Empty(context.Window.OwnedWindows.OfType<NewProjectDialog>());
        }
        finally
        {
            dialog.Close(false);
        }
    }

    [AvaloniaFact]
    public async Task FolderPickerAdapterUsesLocalizedSingleSelectionAndCancellationPreservesLocation()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var storage = System.Reflection.DispatchProxy.Create<IStorageProvider, RecordingFolderStorageProvider>();
        var recorder = (RecordingFolderStorageProvider)storage;
        var service = new WindowWorkbenchDialogService(owner, () => storage);
        owner.Show();
        var answer = service.ShowNewProjectAsync(environment.DirectoryPath,
            (_, _) => Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED)));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<NewProjectDialog>());
        try
        {
            var location = Path.Combine(environment.DirectoryPath, "Workspace 中文 ABC 123");
            UiTestActions.SetText(UiTestActions.Find<TextBox>(dialog, "ProjectLocationInput"), location);
            UiTestActions.Click(dialog, "BrowseProjectLocationButton");
            Assert.NotNull(recorder.Options);
            Assert.False(recorder.Options.AllowMultiple);
            Assert.Equal(Localization.Get("Workbench.BrowseProjectLocation"), recorder.Options.Title);
            Assert.Equal(location, UiTestActions.Find<TextBox>(dialog, "ProjectLocationInput").Text);
            UiTestActions.Click(dialog, "CancelButton");
            Assert.False(await answer);
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkspaceDefaultsAndPathPreviewCreateOneNamedProjectDirectory()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var requests = new List<ProjectCreationRequest>();
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowNewProjectAsync(environment.DirectoryPath, (request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED));
        });
        var dialog = Assert.Single(owner.OwnedWindows.OfType<NewProjectDialog>());
        try
        {
            Assert.Single(dialog.GetLogicalDescendants().OfType<WindowTitleBar>());
            Assert.Equal(Localization.Get("Workbench.Untitled"), UiTestActions.Find<TextBox>(dialog, "ProjectNameInput").Text);
            Assert.Equal(environment.DirectoryPath, UiTestActions.Find<TextBox>(dialog, "ProjectLocationInput").Text);
            UiTestActions.SetText(UiTestActions.Find<TextBox>(dialog, "ProjectNameInput"), "中文 ABC 123");
            var path = Path.Combine(environment.DirectoryPath, "中文 ABC 123", "中文 ABC 123.aeginext");
            Assert.Contains(path, UiTestActions.Find<TextBlock>(dialog, "ProjectPathPreview").Text);

            UiTestActions.Click(dialog, "CreateProjectButton");

            Assert.True(await answer);
            Assert.Equal(new("中文 ABC 123", environment.DirectoryPath), Assert.Single(requests));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task InvalidInputAndFailedCreateKeepTheSameDialogForRetry()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var attempts = 0;
        using var model = new NewProjectDialogViewModel(environment.DirectoryPath, (_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? new ProjectOpenResult(ProjectOpenStatus.FAILED, new IOException("Cannot write the selected directory."))
                : new ProjectOpenResult(ProjectOpenStatus.OPENED));
        }, _ => Task.FromResult<string?>(null));
        var dialog = new NewProjectDialog(model);
        owner.Show();
        var answer = dialog.ShowDialog<bool>(owner);
        try
        {
            UiTestActions.SetText(UiTestActions.Find<TextBox>(dialog, "ProjectNameInput"), "bad/name");
            UiTestActions.Click(dialog, "CreateProjectButton");
            await model.Completion;
            Assert.Equal(0, attempts);
            Assert.True(model.HasError);
            Assert.True(dialog.IsVisible);
            Assert.Equal("bad/name", UiTestActions.Find<TextBox>(dialog, "ProjectNameInput").Text);
            UiTestActions.SetText(UiTestActions.Find<TextBox>(dialog, "ProjectNameInput"), "Retry 中文");
            UiTestActions.Click(dialog, "CreateProjectButton");
            await model.Completion;
            Assert.Equal(1, attempts);
            Assert.True(dialog.IsVisible);
            Assert.Contains("Cannot write", UiTestActions.Find<TextBlock>(dialog, "NewProjectError").Text);
            Assert.Equal("Retry 中文", model.ProjectName);
            UiTestActions.Click(dialog, "CreateProjectButton");

            Assert.True(await answer);
            Assert.Equal(2, attempts);
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCancelsTheCreateTokenAndRecognizesCommittedLateSuccessWithoutReopeningDialog(bool committed)
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var completion = new TaskCompletionSource<ProjectOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = CancellationToken.None;
        var calls = 0;
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowNewProjectAsync(environment.DirectoryPath, (_, cancellation) =>
        {
            calls++;
            token = cancellation;
            return completion.Task;
        });
        var dialog = Assert.Single(owner.OwnedWindows.OfType<NewProjectDialog>());
        var model = Assert.IsType<NewProjectDialogViewModel>(dialog.DataContext);
        var notifications = 0;
        model.Created += (_, _) => notifications++;
        try
        {
            UiTestActions.Click(dialog, "CreateProjectButton");
            Assert.Equal(1, calls);
            Assert.False(UiTestActions.Find<Button>(dialog, "CreateProjectButton").IsEffectivelyEnabled);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.True(token.IsCancellationRequested);
            completion.SetResult(new(committed ? ProjectOpenStatus.OPENED : ProjectOpenStatus.CANCELLED));

            Assert.Equal(committed, await answer);
            Assert.Equal(committed, model.WasCreated);
            Assert.Equal(0, notifications);
            Assert.False(dialog.IsVisible);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            completion.TrySetResult(new(ProjectOpenStatus.CANCELLED));
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task FolderCancellationAndLanguageChangesPreserveDraftsThenEnterCreates()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        var selections = 0;
        using var model = new NewProjectDialogViewModel(environment.DirectoryPath,
            (_, _) => Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED)), _ =>
            {
                selections++;
                return Task.FromResult<string?>(null);
            });
        var dialog = new NewProjectDialog(model);
        owner.Show();
        var answer = dialog.ShowDialog<bool>(owner);
        try
        {
            var input = UiTestActions.Find<TextBox>(dialog, "ProjectNameInput");
            input.Focus();
            dialog.KeyTextInput("项目 ABC 123");
            var name = input.Text;
            UiTestActions.Click(dialog, "BrowseProjectLocationButton");
            await model.Completion;
            Assert.Equal(1, selections);
            Assert.Equal(environment.DirectoryPath, model.ProjectLocation);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.NewProject"), dialog.Title);
            Assert.Equal(name, input.Text);
            Assert.Contains("PingFang SC", input.FontFamily.Name, StringComparison.Ordinal);
            Localization.SetLanguage("en-US");
            input.Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.True(await answer);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            dialog.Close(false);
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExternalCancellationClosesDialogWithoutCallingCreate()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var service = new WindowWorkbenchDialogService(owner);
        owner.Show();
        var answer = service.ShowNewProjectAsync(environment.DirectoryPath, (_, _) =>
        {
            calls++;
            return Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED));
        }, cancellation.Token);
        try
        {
            Assert.Single(owner.OwnedWindows.OfType<NewProjectDialog>());
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            Assert.False(await answer);
            Assert.Equal(0, calls);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<NewProjectDialog> CreateClosedDialog(Window owner, string workspaceRoot)
    {
        var model = new NewProjectDialogViewModel(workspaceRoot,
            (_, _) => Task.FromResult(new ProjectOpenResult(ProjectOpenStatus.OPENED)),
            _ => Task.FromResult<string?>(null));
        var dialog = new NewProjectDialog(model);
        try
        {
            dialog.Show(owner);
            dialog.UpdateLayout();
            return new(dialog);
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Rect BoundsIn(Control control, Visual parent)
    {
        return new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);
    }

    private static void CaptureProjectsSettings(Window owner, string workspace)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY")))
        {
            return;
        }
        var settings = new SettingsWindow(new WorkbenchPreferences
        {
            Projects = new() { WorkspaceRoot = workspace }, Theme = WorkbenchTheme.DARK, Language = "zh-CN"
        });
        try
        {
            settings.SelectPage(SettingsPage.PROJECTS);
            settings.Show(owner);
            CaptureProjectWindow(settings, "settings-projects-zh-CN.png");
        }
        finally
        {
            settings.Close();
        }
    }

    private static void CaptureProjectWindow(Window window, string filename)
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
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
    }
}
