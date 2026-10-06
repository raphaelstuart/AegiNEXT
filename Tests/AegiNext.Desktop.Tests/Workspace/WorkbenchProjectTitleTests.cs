using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkbenchProjectTitleTests
{
    [Fact]
    public void DisplayNameUsesSavedFileThenExplicitNameThenBoundMediaWithoutChangingTheSnapshot()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(Path.GetTempPath(), "字幕测试 01.mp4"));
        var document = new ProjectDocument
        {
            Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        };

        Assert.Equal("字幕测试 01", WorkbenchProjectTitle.GetDisplayName(document, null, "未命名项目"));
        Assert.Equal("Personal", WorkbenchProjectTitle.GetDisplayName(document with { Name = "Personal" }, null, "Untitled"));
        Assert.Equal("项目版本 02", WorkbenchProjectTitle.GetDisplayName(document with { Name = "Personal" },
            Path.Combine(Path.GetTempPath(), "项目版本 02.aeginext"), "Untitled"));
        Assert.Equal("未命名项目", WorkbenchProjectTitle.GetDisplayName(new(), null, "未命名项目"));
        Assert.Equal("Untitled", document.Name);
    }

    [Fact]
    public async Task SaveAsUpdatesTitleOnlyAfterSuccessfulPersistenceAndKeepsTheExistingUndoHistory()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.SetProjectLocation(null, context.DirectoryPath);
        context.Editor.Apply("Edit dimensions", document => document with { Width = 1280 });
        var snapshot = context.Editor.Snapshot;
        var before = session.ViewModel.Title;
        Assert.EndsWith(" •", before, StringComparison.Ordinal);

        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Equal(before, session.ViewModel.Title);
        Assert.Null(session.ProjectPath);
        Assert.Equal(session.ProjectDisplayName + ".aeginext", context.Dialogs.SuggestedSaveName);

        var blocked = Path.Combine(context.DirectoryPath, "blocked.aeginext");
        Directory.CreateDirectory(blocked);
        context.Dialogs.SavePath = blocked;
        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.NotNull(session.LastError);
        Assert.Equal(before, session.ViewModel.Title);
        Assert.Null(session.ProjectPath);
        Assert.Same(snapshot, context.Editor.Snapshot);

        var savedPath = Path.Combine(context.DirectoryPath, "已保存项目.aeginext");
        context.Dialogs.SavePath = savedPath;
        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Null(session.LastError);
        Assert.Equal("AegiNEXT - 已保存项目", session.ViewModel.Title);
        Assert.Equal(savedPath, session.ProjectPath);
        Assert.Equal("Untitled", (await ProjectStore.LoadAsync(savedPath)).Name);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal("Edit dimensions", context.Editor.UndoLabel);
        Assert.False(context.Editor.HasUnsavedChanges);

        Assert.True(context.Editor.Undo());
        Assert.Equal("AegiNEXT - 已保存项目 •", session.ViewModel.Title);
        Assert.True(context.Editor.Redo());
        Assert.Equal("AegiNEXT - 已保存项目", session.ViewModel.Title);

        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "另存版本.aeginext");
        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Equal("AegiNEXT - 另存版本", session.ViewModel.Title);
        Assert.Equal("已保存项目.aeginext", context.Dialogs.SuggestedSaveName);
    }

    [Fact]
    public async Task OpenCancelFailureAndNewProjectPreserveTheCorrectLocalizedName()
    {
        await using var context = new WorkspaceSessionTestContext(new ProjectDocument { Name = "In memory" });
        await context.InitializeAsync();
        var session = context.Session;
        var title = session.ViewModel.Title;
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Equal(title, session.ViewModel.Title);

        var badPath = Path.Combine(context.DirectoryPath, "broken.aeginext");
        await File.WriteAllTextAsync(badPath, "broken JSON");
        context.Dialogs.OpenPath = badPath;
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.NotNull(session.LastError);
        Assert.Equal(title, session.ViewModel.Title);
        Assert.Null(session.ProjectPath);

        var path = Path.Combine(context.DirectoryPath, "文件项目名.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Stored name" }, path);
        context.Dialogs.OpenPath = path;
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Null(session.LastError);
        Assert.Equal("AegiNEXT - 文件项目名", session.ViewModel.Title);

        await session.ExecuteCommandAsync(WorkbenchCommand.NEW_PROJECT);
        Assert.Equal(path, session.ProjectPath);
        Assert.Equal("AegiNEXT - 文件项目名", session.ViewModel.Title);

        var blocked = Path.Combine(context.DirectoryPath, "blocked-new");
        Directory.CreateDirectory(blocked);
        context.Dialogs.NewProjectRequest = new("blocked-new", context.DirectoryPath);
        await session.ExecuteCommandAsync(WorkbenchCommand.NEW_PROJECT);
        Assert.NotNull(context.Dialogs.LastCreationResult!.Error);
        Assert.Equal(path, session.ProjectPath);
        Assert.Equal("Stored name", context.Editor.Snapshot.Name);

        var createdPath = Path.Combine(context.DirectoryPath, "新项目", "新项目.aeginext");
        context.Dialogs.NewProjectRequest = new("新项目", context.DirectoryPath);
        await session.ExecuteCommandAsync(WorkbenchCommand.NEW_PROJECT);
        Assert.Null(session.LastError);
        Assert.Equal(createdPath, session.ProjectPath);
        Assert.True(Directory.Exists(Path.Combine(context.DirectoryPath, "新项目", "backup")));
        Assert.Equal(session.Preferences.Projects.WorkspaceRoot, context.Dialogs.SuggestedWorkspaceRoot);
        Assert.Equal("AegiNEXT - 新项目", session.ViewModel.Title);
        var created = await ProjectStore.LoadAsync(createdPath);
        Assert.Equal("新项目", created.Name);
        Assert.Null(created.Media);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.HasUnsavedChanges);
    }
}
