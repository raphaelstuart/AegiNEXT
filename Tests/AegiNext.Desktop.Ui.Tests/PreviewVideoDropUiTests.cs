using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewVideoDropUiTests
{
    [AvaloniaTheory]
    [InlineData("VideoSurface", "视频 # 01.MKV")]
    [InlineData("EmptyLabel", "video.mp4")]
    [InlineData("QualityCombo", "video.webm")]
    public async Task LocalVideoDroppedOnPreviewOrItsChildrenOpensAndBindsMedia(string targetName, string fileName)
    {
        await using var context = new MainWindowTestContext();
        var path = await CreateFileAsync(context, fileName);
        using var data = await CreateFileDataAsync(context, path);
        var surface = UiTestActions.Find<Grid>(context.Window, "VideoSurface");
        DragEventArgs? feedback = null;
        surface.AddHandler(DragDrop.DragEnterEvent, (_, e) => feedback = e, handledEventsToo: true);
        Assert.True(DragDrop.GetAllowDrop(surface));

        DropOn(context.Window, UiTestActions.Find<Control>(context.Window, targetName), data);
        await WaitForMediaAsync(context, path);

        Assert.NotNull(feedback);
        Assert.Equal(DragDropEffects.Copy, feedback.DragEffects);
        Assert.True(feedback.Handled);
        Assert.Null(context.Session.LastError);
        Assert.True(context.ViewModel.Preview.HasFrame);
        var asset = Assert.Single(context.Session.DocumentSnapshot.Assets, value => value.Kind == ProjectAssetKind.MEDIA);
        Assert.Equal(path, ProjectAssetLocation.Resolve(asset, context.Session.ProjectDirectory));
        Assert.Equal(asset.Id, context.Session.DocumentSnapshot.Media!.AssetId);
        Assert.True(File.Exists(path));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacingVideoKeepsSubtitleEditsAndSupportsUndoRedoInDockedAndFloatingPreview(bool floating)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context, text: "Keep this subtitle");
        var previous = context.Session.DocumentSnapshot;
        var previousPath = context.Controller.Snapshot.FilePath!;
        var path = await CreateFileAsync(context, "replacement.mov");
        using var data = await CreateFileDataAsync(context, path);
        var surface = UiTestActions.Find<Grid>(context.Window, "VideoSurface");
        Window host = context.Window;
        if (floating)
        {
            context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
            Dispatcher.UIThread.RunJobs();
            host = Assert.Single(context.Window.Layouts.FloatingWindows);
            host.UpdateLayout();
        }

        try
        {
            DropOn(host, surface, data);
            await WaitForMediaAsync(context, path);

            Assert.Equal(previous.Subtitles, context.Session.DocumentSnapshot.Subtitles);
            Assert.Single(context.Session.DocumentSnapshot.Assets, value => value.Kind == ProjectAssetKind.MEDIA);
            Assert.Equal("Bind media", context.Session.Editor.UndoLabel);
            Assert.Null(context.Session.LastError);

            await context.Session.ExecuteCommandAsync(WorkbenchCommand.UNDO);
            await WaitForMediaAsync(context, previousPath);
            Assert.Equal(previous.Media, context.Session.DocumentSnapshot.Media);
            Assert.Equal(previous.Subtitles, context.Session.DocumentSnapshot.Subtitles);

            await context.Session.ExecuteCommandAsync(WorkbenchCommand.REDO);
            await WaitForMediaAsync(context, path);
            Assert.Equal(previous.Subtitles, context.Session.DocumentSnapshot.Subtitles);
        }
        finally
        {
            if (floating)
            {
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("subtitle")]
    [InlineData("project")]
    [InlineData("folder")]
    [InlineData("multiple")]
    [InlineData("text")]
    [InlineData("moveOnly")]
    public async Task UnsupportedDropsAreRejectedWithoutChangingMediaOrHistory(string scenario)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var previous = context.Session.DocumentSnapshot;
        var previousPath = context.Controller.Snapshot.FilePath;
        var previousUndo = context.Session.Editor.UndoLabel;
        var path = await CreateFileAsync(context, scenario switch
        {
            "subtitle" => "subtitle.srt",
            "project" => "project.aeginext",
            _ => "video.mp4"
        });
        using var data = new DataTransfer();
        if (scenario == "text")
        {
            data.Add(DataTransferItem.CreateText(path));
        }
        else if (scenario == "folder")
        {
            var directory = Path.Combine(context.Session.ProjectDirectory, "folder.mkv");
            Directory.CreateDirectory(directory);
            var folder = await context.Window.StorageProvider.TryGetFolderFromPathAsync(directory);
            data.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFolder>(folder)));
        }
        else
        {
            var file = await context.Window.StorageProvider.TryGetFileFromPathAsync(path);
            data.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFile>(file)));
            if (scenario == "multiple")
            {
                data.Add(DataTransferItem.CreateFile(file));
            }
        }
        var surface = UiTestActions.Find<Grid>(context.Window, "VideoSurface");
        DragEventArgs? feedback = null;
        surface.AddHandler(DragDrop.DragEnterEvent, (_, e) => feedback = e, handledEventsToo: true);

        DropOn(context.Window, surface, data, scenario == "moveOnly" ? DragDropEffects.Move : DragDropEffects.Copy);
        await context.Session.WaitForProjectIdleAsync();

        Assert.NotNull(feedback);
        Assert.Equal(DragDropEffects.None, feedback.DragEffects);
        Assert.Same(previous, context.Session.DocumentSnapshot);
        Assert.Equal(previousPath, context.Controller.Snapshot.FilePath);
        Assert.Equal(previousUndo, context.Session.Editor.UndoLabel);
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaFact]
    public async Task TransportControlsDoNotAcceptVideoDrops()
    {
        await using var context = new MainWindowTestContext();
        var path = await CreateFileAsync(context, "video.mp4");
        using var data = await CreateFileDataAsync(context, path);
        var surface = UiTestActions.Find<Grid>(context.Window, "VideoSurface");
        var transport = UiTestActions.Find<Grid>(context.Window, "TransportRow");
        var previous = context.Session.DocumentSnapshot;
        var surfacePoint = GetPoint(context.Window, surface);
        var transportPoint = GetPoint(context.Window, transport);

        context.Window.DragDrop(surfacePoint, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        context.Window.DragDrop(transportPoint, RawDragEventType.DragOver, data, DragDropEffects.Copy);
        context.Window.DragDrop(transportPoint, RawDragEventType.Drop, data, DragDropEffects.Copy);
        await context.Session.WaitForProjectIdleAsync();

        Assert.Same(previous, context.Session.DocumentSnapshot);
        Assert.Null(context.Controller.Snapshot.FilePath);
    }

    [AvaloniaFact]
    public async Task DropRechecksWorkspaceAvailabilityAfterDragEntered()
    {
        await using var context = new MainWindowTestContext();
        var path = await CreateFileAsync(context, "video.mp4");
        using var data = await CreateFileDataAsync(context, path);
        var surface = UiTestActions.Find<Grid>(context.Window, "VideoSurface");
        var point = GetPoint(context.Window, surface);
        var previous = context.Session.DocumentSnapshot;
        DragEventArgs? feedback = null;
        surface.AddHandler(DragDrop.DropEvent, (_, e) => feedback = e, handledEventsToo: true);
        context.Window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        using var editLease = context.Session.AcquireEditingLease();
        try
        {
            context.Window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
            Assert.NotNull(feedback);
            Assert.Equal(DragDropEffects.None, feedback.DragEffects);
            Assert.Same(previous, context.Session.DocumentSnapshot);
            Assert.Null(context.Controller.Snapshot.FilePath);
        }
        finally
        {
            editLease.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task FailedVideoDropRestoresPreviousPreviewAndReportsThroughWorkspace()
    {
        var failure = new InvalidDataException("Cannot decode dropped video.");
        await using var context = new MainWindowTestContext(mediaProbe: (path, _) =>
            Path.GetFileName(path) == "broken.mkv"
                ? Task.FromException<VideoPreviewMedia>(failure)
                : Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), VideoWidth: 1, VideoHeight: 1)));
        await context.OpenMediaAsync();
        var previous = context.Session.DocumentSnapshot;
        var previousPath = context.Controller.Snapshot.FilePath!;
        var previousUndo = context.Session.Editor.UndoLabel;
        var path = await CreateFileAsync(context, "broken.mkv");
        using var data = await CreateFileDataAsync(context, path);

        DropOn(context.Window, UiTestActions.Find<Grid>(context.Window, "VideoSurface"), data);
        await DrainAsync(() => ReferenceEquals(context.Session.LastError, failure));
        await WaitForMediaAsync(context, previousPath);

        Assert.Same(previous, context.Session.DocumentSnapshot);
        Assert.Equal(previousUndo, context.Session.Editor.UndoLabel);
        Assert.Equal(failure.Message, context.ViewModel.Error);
        Assert.True(context.ViewModel.Preview.HasFrame);
    }

    private static async Task<string> CreateFileAsync(MainWindowTestContext context, string name)
    {
        Directory.CreateDirectory(context.Session.ProjectDirectory);
        var path = Path.Combine(context.Session.ProjectDirectory, name);
        await File.WriteAllTextAsync(path, "video", TestContext.Current.CancellationToken);
        return path;
    }

    private static async Task<DataTransfer> CreateFileDataAsync(MainWindowTestContext context, string path)
    {
        var file = await context.Window.StorageProvider.TryGetFileFromPathAsync(path);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFile>(file)));
        return data;
    }

    private static void DropOn(Window host, Control target, IDataTransfer data, DragDropEffects effects = DragDropEffects.Copy | DragDropEffects.Move)
    {
        var point = GetPoint(host, target);
        host.DragDrop(point, RawDragEventType.DragEnter, data, effects);
        host.DragDrop(point, RawDragEventType.DragOver, data, effects);
        host.DragDrop(point, RawDragEventType.Drop, data, effects);
    }

    private static Point GetPoint(Window host, Control target)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        return target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host)
            ?? throw new InvalidOperationException("Drop target is not attached to its window.");
    }

    private static async Task WaitForMediaAsync(MainWindowTestContext context, string path)
    {
        await context.Session.WaitForProjectIdleAsync();
        await DrainAsync(() => context.Controller.Snapshot.FilePath == path && context.ViewModel.Preview.HasFrame,
            context.DescribeTaskState);
    }

    private static async Task DrainAsync(Func<bool> complete, Func<string>? describeFailure = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!complete())
        {
            Assert.True(DateTime.UtcNow < deadline,
                "Dropped media did not complete its expected workflow. " + describeFailure?.Invoke());
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
