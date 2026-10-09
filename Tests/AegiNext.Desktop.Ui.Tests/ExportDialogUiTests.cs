using System.Reflection;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证压制所用的原生对话框适配器与错误窗口生命周期。</summary>
public sealed class ExportDialogUiTests
{
    /// <summary>保存适配器将默认目录、文件名和格式传递给系统选择器。</summary>
    [AvaloniaTheory]
    [InlineData(".mp4", true)]
    [InlineData(".mkv", true)]
    [InlineData(".mp4", false)]
    public async Task SavePickerReceivesSuggestedDirectoryAndContainerOptions(string extension, bool suggestDirectory)
    {
        await using var context = new MainWindowTestContext();
        var directory = Path.Combine(context.Session.ProjectDirectory, "output");
        Directory.CreateDirectory(directory);
        var folder = await context.Window.StorageProvider.TryGetFolderFromPathAsync(directory);
        using var storageFolder = Assert.IsAssignableFrom<IStorageFolder>(folder);
        var storage = DispatchProxy.Create<IStorageProvider, RecordingSaveStorageProvider>();
        var recorder = (RecordingSaveStorageProvider)(object)storage;
        recorder.Folder = folder;
        var dialogs = new WindowWorkbenchDialogService(context.Window, () => storage);
        const string FILE_NAME = "项目-20261009-120000";

        var selected = await dialogs.SaveFileAsync("Export", "Videos", ["*.mp4", "*.mkv"], extension,
            FILE_NAME + extension, suggestDirectory ? directory : null);

        Assert.Null(selected);
        var options = Assert.IsType<FilePickerSaveOptions>(recorder.Options);
        Assert.Equal(FILE_NAME, options.SuggestedFileName);
        Assert.Equal(extension.TrimStart('.'), options.DefaultExtension);
        Assert.True(options.ShowOverwritePrompt);
        Assert.Equal<string>(["*.mp4", "*.mkv"], Assert.Single(options.FileTypeChoices!).Patterns);
        if (suggestDirectory)
        {
            Assert.Equal(directory, recorder.RequestedFolderPath!.LocalPath.TrimEnd(Path.DirectorySeparatorChar));
            Assert.Same(folder, options.SuggestedStartLocation);
        }
        else
        {
            Assert.Null(recorder.RequestedFolderPath);
            Assert.Null(options.SuggestedStartLocation);
        }
    }

    /// <summary>错误弹窗保留可复制的原因，实时切换语言并在确认后释放窗口。</summary>
    [AvaloniaFact]
    public async Task ErrorDialogShowsReasonRefreshesLanguageAndClosesOnConfirmation()
    {
        await using var context = new MainWindowTestContext();
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var reason = "Encoder unavailable 中文\n" + new string('x', 1200);
        var operation = service.ShowErrorAsync("Workbench.ExportFailed", reason);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<ErrorDialog>());
        try
        {
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            var details = UiTestActions.Find<TextBox>(dialog, "ErrorDetails");
            Assert.Equal(reason, details.Text);
            Assert.True(details.IsReadOnly);
            Assert.False(operation.IsCompleted);
            Assert.Equal(Localization.Get("Workbench.ExportFailed"), dialog.Title);
            Localization.SetLanguage("zh-CN");
            Assert.Equal("压制失败", dialog.Title);
            Assert.Equal(Localization.Get("Workbench.Confirm"), UiTestActions.Find<Button>(dialog, "ErrorConfirmButton").Content);
            Assert.Equal(Localization.Get("Workbench.ErrorDetailsLogged"),
                UiTestActions.Find<TextBlock>(dialog, "ErrorLoggedMessage").Text);

            UiTestActions.Click(dialog, "ErrorConfirmButton");
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dialog.IsVisible);
            Assert.Empty(context.Window.OwnedWindows.OfType<ErrorDialog>());
            var closedTitle = dialog.Title;
            Localization.SetLanguage("en-US");
            Assert.Equal(closedTitle, dialog.Title);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>工程生命周期取消自动关闭错误弹窗；预取消请求不创建窗口。</summary>
    [AvaloniaFact]
    public async Task CancellationClosesErrorDialogAndPrecancelledRequestDoesNotOpenOne()
    {
        await using var context = new MainWindowTestContext();
        using var cancellation = new CancellationTokenSource();
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var operation = service.ShowErrorAsync("Workbench.ExportFailed", "Failure", cancellation.Token);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<ErrorDialog>());
        try
        {
            await cancellation.CancelAsync();
            Dispatcher.UIThread.RunJobs();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dialog.IsVisible);
            Assert.Empty(context.Window.OwnedWindows.OfType<ErrorDialog>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ShowErrorAsync("Workbench.ExportFailed", "Failure", cancellation.Token));
            Assert.Empty(context.Window.OwnedWindows.OfType<ErrorDialog>());
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>Escape 与窗口关闭均完成错误确认，不残留弹窗。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorDialogCanBeDismissedWithEscapeOrWindowClose(bool escape)
    {
        await using var context = new MainWindowTestContext();
        var service = new WindowWorkbenchDialogService(context.Window);
        var operation = service.ShowErrorAsync("Workbench.ExportFailed", "Failure");
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<ErrorDialog>());
        try
        {
            if (escape)
            {
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            }
            else
            {
                dialog.Close();
            }
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(context.Window.OwnedWindows.OfType<ErrorDialog>());
        }
        finally
        {
            dialog.Close();
        }
    }
}
