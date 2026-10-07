using System.Collections.Immutable;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LayoutPresetBatchDeletionUiTests
{
    [AvaloniaFact]
    public async Task RealManagerSupportsControlAndShiftSelectionAndPreservesSelectionOnRefresh()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var first = (await controller.SaveAsAsync("First"))!;
        var second = (await controller.SaveAsAsync("Second"))!;
        var third = (await controller.SaveAsAsync("Third"))!;
        using var manager = new LayoutPresetManagerWindow(controller);
        try
        {
            owner.Show();
            manager.Show(owner);
            var model = Assert.IsType<LayoutPresetManagerViewModel>(manager.DataContext);
            var list = UiTestActions.Find<ListBox>(manager, "LayoutPresetList");
            var start = WorkspaceLayoutPresets.BuiltIn.Count;
            ClickItem(manager, list, start);
            ClickItem(manager, list, start + 2, RawInputModifiers.Control);
            Assert.Equal(new[] { first, third }, model.SelectedIds);
            Assert.Equal(third, model.Selected!.Id);
            Assert.True(model.DeleteCommand.CanExecute(null));
            Assert.False(model.ApplyCommand.CanExecute(null));
            ClickItem(manager, list, start);
            ClickItem(manager, list, start + 2, RawInputModifiers.Shift);
            Assert.Equal(new[] { first, second, third }, model.SelectedIds);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { first, second, third }, model.SelectedIds);
            Assert.Equal(3, list.Selection.SelectedItems.Count);
            ClickItem(manager, list, 0, RawInputModifiers.Control);
            Assert.False(model.DeleteCommand.CanExecute(null));
        }
        finally
        {
            manager.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task BatchDeletionRemovesAllRequestedPresetsAndPublishesOnce()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var first = (await controller.SaveAsAsync("First"))!;
        var second = (await controller.SaveAsAsync("Second"))!;
        var changed = 0;
        controller.Changed += (_, _) => changed++;
        Assert.True(await controller.DeleteAsync([first, second, first]));
        Assert.Equal(1, changed);
        Assert.All(controller.Presets, preset => Assert.True(preset.IsReadOnly));
        Assert.Equal(WorkspaceLayoutPresets.STANDARD, controller.CurrentPresetId);
        Assert.Empty(new WorkspaceLayoutStore(environment.DirectoryPath).LoadStrict().Presets);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task UnknownOrBuiltInIdRejectsTheEntireBatch()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var id = (await controller.SaveAsAsync("Personal"))!;
        var before = File.ReadAllBytes(Path.Combine(environment.DirectoryPath, "layouts.json"));
        Assert.False(await controller.DeleteAsync([id, "missing"]));
        Assert.False(await controller.DeleteAsync([id, WorkspaceLayoutPresets.STANDARD]));
        Assert.Contains(controller.Presets, preset => preset.Id == id);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(environment.DirectoryPath, "layouts.json")));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ConcurrentRenameAndFlushCannotRestoreDeletedPresetsOnDisk()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var first = (await controller.SaveAsAsync("First"))!;
        var second = (await controller.SaveAsAsync("Second"))!;
        var deletion = controller.DeleteAsync([first]);
        var rename = controller.RenameAsync(second, "Renamed");
        var flush = controller.FlushAsync();
        await Task.WhenAll(deletion, rename, flush);
        var file = new WorkspaceLayoutStore(environment.DirectoryPath).LoadStrict();
        Assert.DoesNotContain(file.Presets, preset => preset.Id == first);
        Assert.Equal("Renamed", Assert.Single(file.Presets).Name);
        Assert.DoesNotContain(controller.Presets, preset => preset.Id == first);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task FailedSaveRetainsAllPresetsAndSelection()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var id = (await controller.SaveAsAsync("Personal"))!;
        var path = Path.Combine(environment.DirectoryPath, "layouts.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        await Assert.ThrowsAnyAsync<IOException>(() => controller.DeleteAsync([id]));
        Assert.Contains(controller.Presets, preset => preset.Id == id);
        Assert.Equal(id, controller.CurrentPresetId);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationFreezesSelectionAndCancellationPreservesPresets(bool accept)
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var first = (await controller.SaveAsAsync("First"))!;
        var second = (await controller.SaveAsAsync("Second"))!;
        var answer = new TaskCompletionSource<bool>();
        PresetDeletionRequest? request = null;
        using var model = new LayoutPresetManagerViewModel(controller, (value, _) =>
        {
            request = value;
            return answer.Task;
        });
        model.SetSelection(first, [first, second]);
        var operation = model.DeleteCommand.ExecuteAsync(null);
        Assert.Equal(ImmutableArray.Create("First", "Second"), request!.Names);
        model.SetSelection(WorkspaceLayoutPresets.STANDARD, [WorkspaceLayoutPresets.STANDARD]);
        answer.SetResult(accept);
        await operation;
        Assert.Equal(!accept, controller.Presets.Any(preset => preset.Id == first));
        Assert.Equal(!accept, controller.Presets.Any(preset => preset.Id == second));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task DisposedManagerRejectsLateConfirmationAndBuiltInMixedSelection()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        using var controller = CreateController(owner, environment.DirectoryPath);
        var id = (await controller.SaveAsAsync("Personal"))!;
        var answer = new TaskCompletionSource<bool>();
        var model = new LayoutPresetManagerViewModel(controller, (_, _) => answer.Task);
        model.SetSelection(id, [id, WorkspaceLayoutPresets.STANDARD]);
        Assert.False(model.DeleteCommand.CanExecute(null));
        model.SetSelection(id, [id]);
        var operation = model.DeleteCommand.ExecuteAsync(null);
        model.Dispose();
        answer.SetResult(true);
        await operation;
        Assert.Contains(controller.Presets, preset => preset.Id == id);
        owner.Close();
    }

    private static WorkbenchLayoutController CreateController(Window owner, string directory)
    {
        return new(owner, WorkbenchPanelIds.All.ToDictionary(id => id, _ => (Control)new TextBox()),
            directory, () => true, () => { }, _ => { });
    }

    private static void ClickItem(Window window, ListBox list, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
        item.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }
}
