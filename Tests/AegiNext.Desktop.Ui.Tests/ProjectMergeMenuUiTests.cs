using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ProjectMergeMenuUiTests
{
    [AvaloniaFact]
    public async Task NativeAndWindowMenusUseTheMergeCommandAndRefreshItsLabelWithoutEditingTheProject()
    {
        await using var context = new MainWindowTestContext();
        var command = context.Window.GetCommand(WorkbenchCommand.MERGE_PROJECT);
        var snapshot = context.Session.Editor.Snapshot;
        var native = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(context.Window));
        var file = native.Items.OfType<NativeMenuItem>().Single(item => item.Header == "File");
        var item = Assert.Single(file.Menu!.Items.OfType<NativeMenuItem>(),
            value => ReferenceEquals(value.Command, command));
        Assert.Equal("Merge other projects…", item.Header);
        Assert.NotNull(item.Icon);
        Assert.True(command.CanExecute(null));

        Localization.SetLanguage("zh-CN");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("合并其他工程…", item.Header);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);

        context.Session.UpdatePreferences(context.Session.Preferences with { WindowMenuOnMac = true });
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var menu = context.Window.GetVisualDescendants().OfType<Menu>().Single(value => value.Name == "MainMenu");
        var windowFile = menu.Items.OfType<MenuItem>().Single(value => Equals(value.Header, "文件"));
        var windowItem = Assert.Single(windowFile.Items.OfType<MenuItem>(),
            value => ReferenceEquals(value.Command, command));
        Assert.Equal("合并其他工程…", windowItem.Header);
        Assert.Equal(MaterialIconKind.CallMerge, Assert.IsType<MaterialIcon>(windowItem.Icon).Kind);

        Localization.SetLanguage("en-US");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Merge other projects…", windowItem.Header);
        Assert.Same(command, windowItem.Command);
        Assert.Same(snapshot, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }
}
