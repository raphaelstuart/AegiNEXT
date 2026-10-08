using AegiNext.Application;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class QueuedProjectSaveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedSaveUsesFrozenSnapshotAndPreservesNewerContentAndInvalidDraft(bool relocate)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        var coordinator = new ProjectWorkflowCoordinator(session, context.Dialogs);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "original", "project.aeginext");
        Assert.True(await coordinator.SaveProjectAsync(false));
        context.Editor.AddSubtitle(new(0), new(1), "frozen");
        var source = context.Editor.Snapshot;
        var subtitleId = source.Subtitles[0].Id;
        var gate = new QueuedSaveGateTask(session.TaskScope);
        var gateHandle = session.ApplicationContext.Tasks.Submit(gate);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var destination = relocate ? Path.Combine(context.DirectoryPath, "relocated", "copy.aeginext") : session.ProjectPath!;
        context.Dialogs.SavePath = destination;
        var saving = coordinator.SaveProjectAsync(relocate);
        try
        {
            Assert.Contains(session.ApplicationContext.Tasks.GetSnapshots(), task => task.Name == "Tasks.SaveProject" && !task.IsFinished);
            context.Editor.UpdateSubtitle(subtitleId, line => line with { Text = "newer" });
            var row = session.ViewModel.Subtitles.Rows.Single();
            row.StartText = "invalid";
            gate.Released.TrySetResult();
            await gateHandle.Completion;
            Assert.True(await saving.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("frozen", (await ProjectStore.LoadAsync(destination)).Subtitles[0].Text);
            Assert.Equal("newer", context.Editor.Snapshot.Subtitles[0].Text);
            Assert.Same(row, session.ViewModel.Subtitles.Rows.Single());
            Assert.Equal("invalid", row.StartText);
            Assert.True(session.HasUnsavedChanges);
            Assert.True(session.HasProjectDrafts);
            Assert.Equal(!relocate, context.Editor.CanUndo);
        }
        finally
        {
            gate.Released.TrySetResult();
            await saving;
        }
    }
}
