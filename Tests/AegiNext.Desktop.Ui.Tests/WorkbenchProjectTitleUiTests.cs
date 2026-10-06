using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchProjectTitleUiTests
{
    [AvaloniaFact]
    public async Task MediaProjectDirtyAndLanguageChangesSynchronizeMainAndFloatingNativeAndCustomTitles()
    {
        await using var context = new MainWindowTestContext();
        AssertTitles(context, "AegiNEXT - " + Localization.Get("Workbench.Untitled"));
        Directory.CreateDirectory(context.Session.ScratchDirectory);
        var mediaPath = Path.Combine(context.Session.ScratchDirectory, "中文字幕 01.mp4");
        await File.WriteAllBytesAsync(mediaPath, new byte[20], TestContext.Current.CancellationToken);
        await context.Window.OpenMediaAsync(mediaPath, true);
        AssertTitles(context, "AegiNEXT - 中文字幕 01 •");
        Assert.Equal("Untitled", context.Session.DocumentSnapshot.Name);

        context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            AssertTitles(context, "AegiNEXT - 中文字幕 01 •");
            var stored = Path.Combine(context.Session.ProjectDirectory, "文件工程名.aeginext");
            context.Session.SetProjectLocation(stored, context.Session.ProjectDirectory);
            AssertTitles(context, "AegiNEXT - 文件工程名 •");
            context.Session.Editor.MarkSaved();
            AssertTitles(context, "AegiNEXT - 文件工程名");
            context.Session.Editor.AddSubtitle(new(0), new(1), "字幕");
            AssertTitles(context, "AegiNEXT - 文件工程名 •");
            Assert.True(context.Session.Editor.Undo());
            AssertTitles(context, "AegiNEXT - 文件工程名");
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
            AssertTitles(context, "AegiNEXT - 文件工程名");
            Assert.EndsWith("视频预览", floating.Title, StringComparison.Ordinal);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertTitles(MainWindowTestContext context, string expected)
    {
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        Assert.Equal(expected, context.ViewModel.Title);
        Assert.Equal(expected, context.Window.Title);
        foreach (var window in context.WindowRegistry.Windows)
        {
            if (!ReferenceEquals(window, context.Window))
            {
                Assert.StartsWith(expected + " — ", window.Title, StringComparison.Ordinal);
            }

            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<WindowTitleBar>());
            Assert.Equal(window.Title, titleBar.Title);
            Assert.Equal(window.Title, UiTestActions.Find<TextBlock>(titleBar, "TitleText").Text);
        }
    }
}
