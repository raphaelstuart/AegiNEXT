using System.Security.Cryptography;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

/// <summary>验证多人分段工程合并的事务、资源和桌面生命周期边界。</summary>
[Collection("Workspace session")]
public sealed class ProjectMergeWorkflowTests
{
    private const string IMAGE_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=";

    /// <summary>同一模板的多个分支应保留原时间和合成树，并作为一次撤销事务导入。</summary>
    [Fact]
    public async Task TemplateBranchesKeepIndependentTracksOriginalTimesAndCompleteGroupsWithOneUndoRedo()
    {
        var template = CreateTemplate();
        await using var context = new WorkspaceSessionTestContext(template);
        await context.InitializeAsync();
        var sourceA = CreateBranch(template, "First member", new(11, 3));
        var sourceB = CreateBranch(template, "Second member", new(23, 3));
        var pathA = await WriteSourceAsync(context.DirectoryPath, "member-a", sourceA);
        var pathB = await WriteSourceAsync(context.DirectoryPath, "member-b", sourceB);
        context.Dialogs.OpenPaths = [pathA, pathB];
        var originalDirectory = context.Session.ProjectDirectory;
        var originalViewState = context.Session.TimelineViewState;
        var changes = 0;
        context.Editor.Changed += (_, _) => changes++;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Null(context.Session.LastError);
        var merged = context.Editor.Snapshot;
        Assert.Equal(3, merged.SubtitleTracks.Length);
        Assert.Equal(template.SubtitleTracks, merged.SubtitleTracks.Take(template.SubtitleTracks.Length));
        Assert.Equal(template.Subtitles, merged.Subtitles.Take(template.Subtitles.Length));
        Assert.Equal(template.Layers, merged.Layers.Take(template.Layers.Length));
        Assert.Equal(3, merged.SubtitleTracks.Select(track => track.Id).Distinct().Count());
        Assert.Equal(3, merged.Subtitles.Select(cue => cue.Id).Distinct().Count());
        Assert.Equal(3, merged.Subtitles.Select(cue => cue.TrackId).Distinct().Count());
        Assert.Equal(template.Assets.Length + 1, merged.Assets.Length);
        Assert.Equal(12, merged.Layers.SelectMany(group => group.Children.Select(child => child.Id).Prepend(group.Id))
            .Distinct().Count());
        AssertImportedBranch(sourceA, merged, merged.Subtitles[1], merged.Layers[1], context.Session.ProjectDirectory);
        AssertImportedBranch(sourceB, merged, merged.Subtitles[2], merged.Layers[2], context.Session.ProjectDirectory);
        Assert.Equal(template.Id, merged.Id);
        Assert.Equal(template.Name, merged.Name);
        Assert.Equal(template.FrameRate, merged.FrameRate);
        Assert.Equal(template.ReferenceWhiteNits, merged.ReferenceWhiteNits);
        Assert.Null(context.Session.ProjectPath);
        Assert.Equal(originalDirectory, context.Session.ProjectDirectory);
        Assert.Same(originalViewState, context.Session.TimelineViewState);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
        Assert.Equal(ProjectStore.Serialize(sourceA), await File.ReadAllBytesAsync(pathA));
        Assert.Equal(ProjectStore.Serialize(sourceB), await File.ReadAllBytesAsync(pathB));
        Assert.Equal(1, changes);
        Assert.True(context.Editor.Undo());
        Assert.Same(template, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(merged, context.Editor.Snapshot);
        Assert.False(context.Editor.CanRedo);
        Assert.All(merged.Assets.Skip(template.Assets.Length), asset =>
            Assert.True(File.Exists(ProjectAssetLocation.Resolve(asset, originalDirectory))));
    }

    /// <summary>不同帧率的源工程以原有绝对时间导入，不修改目标帧率。</summary>
    [Fact]
    public async Task DifferentFrameRateKeepsRationalCueTimesAndTargetFrameRate()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var source = CreateTemplate() with { FrameRate = new(24000, 1001) };
        source = CreateBranch(source, "Rational timing", new(1001, 24000));
        context.Dialogs.OpenPaths = [await WriteSourceAsync(context.DirectoryPath, "rate", source)];
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Null(context.Session.LastError);
        var imported = Assert.Single(context.Editor.Snapshot.Subtitles);
        Assert.Equal(source.Subtitles[0].Start, imported.Start);
        Assert.Equal(source.Subtitles[0].End, imported.End);
        Assert.Equal(original.FrameRate, context.Editor.Snapshot.FrameRate);
    }

    /// <summary>取消文件选择不改变快照、撤销历史或工程列表。</summary>
    [Fact]
    public async Task EmptyPickerSelectionLeavesSnapshotAndHistoryUntouched()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.IsProjectBusy);
        Assert.Equal(1, context.Dialogs.OpenFilesRequests);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    /// <summary>多源文件中出现格式错误时，整个合并都不提交。</summary>
    [Fact]
    public async Task ValidFirstSourceAndInvalidSecondSourceDoNotPartiallyMerge()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var valid = await WriteSourceAsync(context.DirectoryPath, "valid", CreateTemplate());
        var broken = Path.Combine(context.DirectoryPath, "broken.aeginext");
        await File.WriteAllTextAsync(broken, "invalid project JSON");
        context.Dialogs.OpenPaths = [valid, broken];
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.NotNull(context.Session.LastError);
        AssertRejected(context, original);
    }

    /// <summary>缺失或哈希不符的实际依赖资源导致整批失败且清理临时文件。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrCorruptReferencedImageRejectsAllSourcesAndLeavesNoPublishedAssets(bool corruptHash)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var valid = await WriteSourceAsync(context.DirectoryPath, "valid", CreateTemplate());
        var source = CreateTemplate();
        if (corruptHash)
        {
            source = source with { Assets = [source.Assets[0] with { Sha256 = new string('0', 64) }] };
        }
        var broken = await WriteSourceAsync(context.DirectoryPath, "broken", source, corruptHash);
        context.Dialogs.OpenPaths = [valid, broken];
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.NotNull(context.Session.LastError);
        AssertRejected(context, original);
        var destination = context.Session.ProjectDirectory;
        Assert.Empty(Directory.Exists(destination)
            ? Directory.GetFiles(destination, "*", SearchOption.AllDirectories) : []);
    }

    /// <summary>不兼容的画布或参考白导致整个批次失败。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentCanvasOrReferenceWhiteRejectsTheWholeBatch(bool differentWhite)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var valid = await WriteSourceAsync(context.DirectoryPath, "valid", CreateTemplate());
        var source = CreateTemplate();
        source = differentWhite ? source with { ReferenceWhiteNits = 100 } : source with { Width = 1280 };
        var incompatible = await WriteSourceAsync(context.DirectoryPath, "incompatible", source);
        context.Dialogs.OpenPaths = [valid, incompatible];

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.NotNull(context.Session.LastError);
        AssertRejected(context, original);
        Assert.Empty(Directory.Exists(context.Session.ProjectDirectory)
            ? Directory.GetFiles(context.Session.ProjectDirectory, "*", SearchOption.AllDirectories) : []);
    }

    /// <summary>源媒体和无引用的坏资源不会进入目标工程或阻断有效内容。</summary>
    [Fact]
    public async Task SourceMediaAndUnusedMissingImageAreExcludedWithoutReadingThem()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var source = CreateTemplate();
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing-video.mkv");
        var unusedImage = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing-unused.png", new string('0', 64));
        source = source with
        {
            Assets = source.Assets.Add(media).Add(unusedImage),
            Media = new(media.Id, 0, null, MediaTime.Zero)
        };
        context.Dialogs.OpenPaths = [await WriteSourceAsync(context.DirectoryPath, "references", source)];

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Null(context.Editor.Snapshot.Media);
        Assert.Equal(ProjectAssetKind.IMAGE, Assert.Single(context.Editor.Snapshot.Assets).Kind);
        Assert.Single(context.Editor.Snapshot.Subtitles);
        Assert.Equal(0, context.Dialogs.UnavailableMediaRequests);
    }

    /// <summary>合并和保存重开保留当前媒体、工程位置和独立复制的图片。</summary>
    [Fact]
    public async Task MergeSaveAndReopenKeepCurrentMediaAndCopiedResourcesAfterSourceRemoval()
    {
        using var target = new TemporaryWorkbenchDirectory();
        using var sources = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var media = Path.Combine(target.Path, "target-video.mkv");
        await File.WriteAllTextAsync(media, "media fixture");
        await context.Session.OpenMediaAsync(media, true);
        context.Dialogs.SavePath = Path.Combine(target.Path, "target.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        await context.Session.Controller.SeekAsync(new(1, 10));
        var original = context.Session.Editor.Snapshot;
        var position = context.Session.Controller.Snapshot.Position;
        var history = context.Session.ApplicationContext.RecentProjects.Entries.ToArray();
        var probeCount = context.ProbePaths.Count;
        var timelineState = context.Session.TimelineViewState;
        var source = CreateTemplate() with { Width = 1, Height = 1 };
        var sourceMedia = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing-source.mkv");
        source = source with
        {
            Assets = source.Assets.Add(sourceMedia), Media = new(sourceMedia.Id, 0, null, MediaTime.Zero)
        };
        var path = await WriteSourceAsync(sources.Path, "member", source);
        context.Dialogs.OpenPaths = [path];

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(original.Media, context.Session.Editor.Snapshot.Media);
        Assert.Equal(original.Assets[0], context.Session.Editor.Snapshot.Assets[0]);
        Assert.Equal(media, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(position, context.Session.Controller.Snapshot.Position);
        Assert.Equal(probeCount, context.ProbePaths.Count);
        Assert.Equal(context.Dialogs.SavePath, context.Session.ProjectPath);
        Assert.Equal(target.Path, context.Session.ProjectDirectory);
        Assert.Equal(history, context.Session.ApplicationContext.RecentProjects.Entries);
        Assert.Same(timelineState, context.Session.TimelineViewState);
        Assert.Equal(1, context.Session.Editor.Snapshot.Assets.Count(asset => asset.Kind == ProjectAssetKind.MEDIA));
        var importedAsset = Assert.Single(context.Session.Editor.Snapshot.Assets.Where(asset => asset.Kind == ProjectAssetKind.IMAGE));
        Assert.Equal(ImageBytes(), await File.ReadAllBytesAsync(ProjectAssetLocation.Resolve(importedAsset, target.Path)));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        Assert.Null(context.Session.LastError);
        context.Dialogs.OpenPath = context.Session.ProjectPath;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(media, context.Session.Controller.Snapshot.FilePath);
        Assert.Single(context.Session.Editor.Snapshot.Subtitles);
        var savedImage = Assert.Single(context.Session.Editor.Snapshot.Assets.Where(asset => asset.Kind == ProjectAssetKind.IMAGE));
        Assert.Equal(importedAsset, savedImage);
        Assert.Equal(ImageBytes(), await File.ReadAllBytesAsync(ProjectAssetLocation.Resolve(savedImage, target.Path)));
    }

    /// <summary>无效草稿阻断合并且保持正在编辑的文本。</summary>
    [Fact]
    public async Task InvalidSubtitleDraftBlocksThePickerAndPreservesDraftAndHistory()
    {
        var template = CreateTemplate();
        await using var context = new WorkspaceSessionTestContext(template);
        await context.InitializeAsync();
        context.Session.SelectCue(template.Subtitles[0].Id);
        context.Session.ViewModel.Subtitles.Rows[0].EndText = "invalid";
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.Same(original, context.Editor.Snapshot);
        Assert.Equal("invalid", context.Session.ViewModel.Subtitles.Rows[0].EndText);
        Assert.Equal(0, context.Dialogs.OpenFilesRequests);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.IsProjectBusy);
    }

    /// <summary>文件选择期间的直接模型变更不会被过期合并覆盖。</summary>
    [Fact]
    public async Task ChangedSnapshotWhilePickerIsOpenCancelsMergeWithoutOverwritingTheNewEdit()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var path = await WriteSourceAsync(context.DirectoryPath, "member", CreateTemplate());
        context.Dialogs.PendingOpenFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);
        await context.Dialogs.OpenFilesShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.Editor.Apply("Concurrent edit", document => document with { Name = "Edited while selecting" });
        var changed = context.Editor.Snapshot;
        var undo = context.Editor.UndoLabel;
        context.Dialogs.PendingOpenFiles.SetResult([path]);

        await pending;

        Assert.IsType<InvalidOperationException>(context.Session.LastError);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Equal(undo, context.Editor.UndoLabel);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
        Assert.False(context.Session.IsProjectBusy);
        Assert.True(context.Editor.Undo());
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>合并选择文件期间禁止第二次合并和直接接口重入。</summary>
    [Fact]
    public async Task PendingPickerDisablesCommandsAndRejectsReentrantMerge()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Dialogs.PendingOpenFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);
        await context.Dialogs.OpenFilesShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(context.Session.IsProjectBusy);
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.MERGE_PROJECT));
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.OPEN_PROJECT));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);
        await context.Session.MergeProjectsAsync([Path.Combine(context.DirectoryPath, "missing.aeginext")]);
        Assert.Equal(1, context.Dialogs.OpenFilesRequests);
        context.Dialogs.PendingOpenFiles.SetResult([]);
        await pending;

        Assert.Null(context.Session.LastError);
        AssertRejected(context, original);
    }

    /// <summary>关闭会解除挂起的文件选择并取消尚未提交的合并。</summary>
    [Fact]
    public async Task DisposingSessionCancelsPendingPickerWithoutPublishingChanges()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Dialogs.PendingOpenFiles = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);
        await context.Dialogs.OpenFilesShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await context.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        context.Dialogs.PendingOpenFiles.TrySetResult([]);

        Assert.Null(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Session.IsClosing);
        Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.MERGE_PROJECT));
    }

    /// <summary>预先取消的直接合并不读取文件也不改变目标工程。</summary>
    [Fact]
    public async Task PreCancelledDirectMergeLeavesSnapshotAndErrorUntouched()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await context.Session.MergeProjectsAsync([Path.Combine(context.DirectoryPath, "missing.aeginext")], cancellation.Token);

        Assert.Null(context.Session.LastError);
        AssertRejected(context, original);
        Assert.Equal(0, context.Dialogs.OpenFilesRequests);
    }

    /// <summary>重复源路径必须整体拒绝，避免意外重复导入同一成员的内容。</summary>
    [Fact]
    public async Task DuplicateSourcePathsAreRejectedBeforeAnyMutation()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var path = await WriteSourceAsync(context.DirectoryPath, "member", CreateTemplate());
        context.Dialogs.OpenPaths = [path, path];
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.NotNull(context.Session.LastError);
        AssertRejected(context, original);
    }

    /// <summary>活动工程和恢复备份都不能作为普通合并源。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveProjectOrBackupSourceIsRejected(bool backup)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        await context.Session.CreateProjectAsync(new ProjectCreationRequest("active", context.DirectoryPath));
        var path = context.Session.ProjectPath!;
        if (backup)
        {
            path = await ProjectBackupStore.WriteAsync(context.Editor.Snapshot, path, DateTimeOffset.UtcNow, 20);
        }
        context.Dialogs.OpenPaths = [path];
        var original = context.Editor.Snapshot;
        var history = context.Session.ApplicationContext.RecentProjects.Entries.ToArray();

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.MERGE_PROJECT);

        Assert.NotNull(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.IsProjectBusy);
        Assert.Equal(history, context.Session.ApplicationContext.RecentProjects.Entries);
    }

    private static ProjectDocument CreateTemplate()
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2), Text = "Original" };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "image.png",
            Convert.ToHexStringLower(SHA256.HashData(ImageBytes())));
        var subtitle = new ProjectLayer
        {
            Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
        };
        var shape = new ProjectLayer
        {
            Name = "Shape", Kind = LayerKind.SHAPE, Start = new(1), End = new(3),
            Shape = new(ShapeKind.RECTANGLE, 30, 40), Opacity = 0.5,
            Transform = new() { X = 25, Y = 35, Rotation = 12 }
        };
        var image = new ProjectLayer
        {
            Name = "Image", Kind = LayerKind.IMAGE, Start = new(3), End = new(4), Image = new(asset.Id, 1, 1)
        };
        var group = new ProjectLayer
        {
            Name = "Composition", Kind = LayerKind.GROUP, Start = MediaTime.Zero, End = new(60),
            Children = [subtitle, shape, image], Transform = new() { X = 15, Y = 25, ScaleX = 0.75, ScaleY = 0.75 }
        };
        return new() { Name = "Shared template", Assets = [asset], Subtitles = [cue], Layers = [group] };
    }

    private static ProjectDocument CreateBranch(ProjectDocument template, string text, MediaTime start)
    {
        var cue = template.Subtitles[0] with { Start = start, End = start + new MediaTime(5, 3), Text = text };
        var group = template.Layers[0];
        var subtitle = group.Children[0] with { Start = cue.Start, End = cue.End };
        return template with { Subtitles = [cue], Layers = [group with { Children = group.Children.SetItem(0, subtitle) }] };
    }

    private static async Task<string> WriteSourceAsync(string rootDirectory, string name, ProjectDocument document,
        bool writeImage = true)
    {
        var directory = Path.Combine(rootDirectory, name);
        Directory.CreateDirectory(directory);
        if (writeImage)
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "image.png"), ImageBytes());
        }
        var path = Path.Combine(directory, name + ".aeginext");
        await ProjectStore.SaveAsync(document, path);
        return path;
    }

    private static void AssertImportedBranch(ProjectDocument source, ProjectDocument merged, SubtitleLine cue,
        ProjectLayer group, string destination)
    {
        var originalCue = source.Subtitles[0];
        Assert.Equal(originalCue with { Id = cue.Id, TrackId = cue.TrackId }, cue);
        var originalGroup = source.Layers[0];
        Assert.NotEqual(originalGroup.Id, group.Id);
        Assert.Equal(originalGroup with { Id = group.Id, Children = group.Children }, group);
        Assert.Equal(3, group.Children.Length);
        Assert.Equal(cue.Id, group.Children[0].SubtitleId);
        Assert.Equal(cue.Start, group.Children[0].Start);
        Assert.Equal(cue.End, group.Children[0].End);
        Assert.Equal(originalGroup.Children[0] with { Id = group.Children[0].Id, SubtitleId = cue.Id }, group.Children[0]);
        Assert.Equal(originalGroup.Children[1] with { Id = group.Children[1].Id }, group.Children[1]);
        var image = group.Children[2];
        var asset = merged.Assets.Single(item => item.Id == image.Image!.AssetId);
        Assert.Equal(ProjectAssetKind.IMAGE, asset.Kind);
        Assert.NotEqual(source.Assets[0].Id, asset.Id);
        Assert.Equal(originalGroup.Children[2] with { Id = image.Id, Image = image.Image }, image);
        Assert.Equal(source.Layers[0].Children[2].Image! with { AssetId = asset.Id }, image.Image);
        Assert.True(File.Exists(ProjectAssetLocation.Resolve(asset, destination)));
        Assert.Equal(ImageBytes(), File.ReadAllBytes(ProjectAssetLocation.Resolve(asset, destination)));
        ProjectValidator.Validate(merged);
    }

    private static void AssertRejected(WorkspaceSessionTestContext context, ProjectDocument original)
    {
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.IsProjectBusy);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    private static byte[] ImageBytes() => Convert.FromBase64String(IMAGE_BASE64);
}
