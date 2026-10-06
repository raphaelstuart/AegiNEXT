using AegiNext.Desktop.I18n;
using AegiNext.Application;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkbenchWorkflowLogTests
{
    [Fact]
    public async Task ProjectOpenReportsCompletionOnlyAfterSuccessfulLoadAndKeepsFullFailure()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.Journal.Clear();
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.DoesNotContain(session.Journal.Entries, entry => entry.Source == "Project");

        var invalidPath = Path.Combine(context.DirectoryPath, "invalid.aeginext");
        await File.WriteAllTextAsync(invalidPath, "invalid JSON");
        context.Dialogs.OpenPath = invalidPath;
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.DoesNotContain(session.Journal.Entries, entry => entry.Source == "Project");
        var error = Assert.IsAssignableFrom<Exception>(session.LastError);
        Assert.Contains(session.Journal.Entries, entry => entry.Level == WorkbenchLogLevel.ERROR && entry.Details == error.ToString());

        var path = Path.Combine(context.DirectoryPath, "successful.aeginext");
        var project = new ProjectDocument { Name = "Loaded successfully" };
        await ProjectStore.SaveAsync(project, path);
        context.Dialogs.OpenPath = path;
        await session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        var completed = Assert.Single(session.Journal.Entries, entry => entry.Source == "Project");
        Assert.Equal(WorkbenchLogLevel.INFO, completed.Level);
        Assert.Equal(Localization.Get("WorkflowLog.ProjectOpened"), completed.Message);
        Assert.Equal(path, completed.Details);
        Assert.Equal(project.Id, session.DocumentSnapshot.Id);
    }

    [Fact]
    public async Task StyleSaveReportsSuccessAfterPersistenceAndRejectedConflictDoesNotReportCompletion()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.Journal.Clear();
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Existing", new());
        await session.RunCommandAsync(() => session.Styles.UpsertAsync(preset));
        var completed = Assert.Single(session.Journal.Entries, entry => entry.Source == "Styles");
        Assert.Equal(WorkbenchLogLevel.INFO, completed.Level);
        Assert.Equal(Localization.Get("WorkflowLog.StyleSaved"), completed.Message);
        Assert.Equal(preset.Name, completed.Details);
        Assert.Equal(preset, Assert.Single(session.StyleLibrary.Snapshot.Presets));

        session.Journal.Clear();
        await session.RunCommandAsync(() => session.Styles.UpsertAsync(preset with { Id = Guid.NewGuid() }));
        Assert.DoesNotContain(session.Journal.Entries, entry => entry.Source == "Styles");
        var error = Assert.IsAssignableFrom<Exception>(session.LastError);
        Assert.Contains(session.Journal.Entries, entry => entry.Level == WorkbenchLogLevel.ERROR && entry.Details == error.ToString());
        Assert.Equal(preset, Assert.Single(session.StyleLibrary.Snapshot.Presets));
    }
}
