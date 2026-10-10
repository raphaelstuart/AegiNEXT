using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssFontWeightWorkflowTests
{
    [Fact]
    public async Task ExactCatalogFaceIsStoredByTheImportWorkflowInOneUndo()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var face = new SystemFontFace
        {
            FamilyName = "Fixture",
            Variant = new() { Name = "SemiBold", PostScriptName = "Fixture-SemiBold", Weight = 600 }
        };
        var fonts = new SubtitleFontSelectionService(new[] { face });
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        var original = context.Editor.Snapshot;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(context.Session.LastError);
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        var style = line.InlineSpans.FirstOrDefault()?.Style.ApplyTo(line.Style) ?? line.Style;
        Assert.Equal(face.Variant, style.FontVariant);
        Assert.False(style.Bold);
        Assert.DoesNotContain(context.Dialogs.ConversionDiagnostics,
            value => value.StartsWith("Ass.FontWeight:", StringComparison.Ordinal));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task FailedCatalogStillRequiresWeightConversionAndImportsInOneUndo()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var fonts = new SubtitleFontSelectionService(() => Task.FromException<SystemFontCatalog>(new IOException("font catalog unavailable")));
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        var original = context.Editor.Snapshot;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(context.Session.LastError);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, value => value.StartsWith("Ass.FontWeight:", StringComparison.Ordinal));
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        var style = line.InlineSpans.FirstOrDefault()?.Style.ApplyTo(line.Style) ?? line.Style;
        Assert.Null(style.FontVariant);
        Assert.True(style.Bold);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CancelledCatalogDoesNotBecomeAnApprovedWeightFallback()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var fonts = new SubtitleFontSelectionService(() => Task.FromCanceled<SystemFontCatalog>(new(true)));
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        var original = context.Editor.Snapshot;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(context.Session.LastError);
        Assert.Null(context.Dialogs.ConversionReview);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(AegiTaskState.Cancelled, ImportSnapshot(application.Tasks).State);
    }

    [Fact]
    public async Task DecliningWeightConversionLeavesTheProjectAndUndoUntouched()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var fonts = new SubtitleFontSelectionService(SystemFontCatalog.Empty);
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        var original = context.Editor.Snapshot;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(context.Dialogs.ConversionDiagnostics, value => value.StartsWith("Ass.FontWeight:", StringComparison.Ordinal));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SingleExecutionSlotQueuesCatalogRetryBeforeImportWithoutNestedSubmission()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var catalogTask = new AssFontCatalogGateTask();
        AegiTaskService? tasks = null;
        var requests = 0;
        var fonts = new SubtitleFontSelectionService(() => ++requests == 1
            ? Task.FromException<SystemFontCatalog>(new IOException("retry catalog"))
            : tasks!.Submit(catalogTask).Completion);
        await using var application = CreateApplication(directory.Path, fonts);
        tasks = application.Tasks;
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        application.Tasks.MaximumConcurrentTasks = 1;
        context.Dialogs.ConversionChoice = true;
        var original = context.Editor.Snapshot;
        var operation = context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);
        try
        {
            await catalogTask.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var catalog = Assert.Single(tasks.GetSnapshots(), snapshot => snapshot.Name == catalogTask.Name && !snapshot.IsFinished);
            var import = ImportSnapshot(tasks);
            Assert.Equal(AegiTaskState.Running, catalog.State);
            Assert.Equal(AegiTaskState.Queued, import.State);
            Assert.True(catalog.SubmissionSequence < import.SubmissionSequence);
            catalogTask.Released.SetResult();

            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, requests);
            Assert.Null(context.Session.LastError);
            Assert.Equal(AegiTaskState.Succeeded, ImportSnapshot(tasks).State);
            Assert.Single(context.Editor.Snapshot.Subtitles);
            Assert.True(context.Editor.Undo());
            Assert.Same(original, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
        }
        finally
        {
            catalogTask.Released.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task CancellingImportWhileCatalogIsPendingLeavesSharedLoadingAndProjectIntact()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var pending = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fonts = new SubtitleFontSelectionService(() => pending.Task);
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        var original = context.Editor.Snapshot;
        var sharedLoading = fonts.EnsureLoadedAsync();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var operation = workflow.ImportSubtitlesAsync(true);
        try
        {
            var import = await WaitForRunningImportAsync(application.Tasks);
            Assert.False(sharedLoading.IsCompleted);
            Assert.True(application.Tasks.RequestCancel(import.Id));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.False(sharedLoading.IsCompleted);
            Assert.Same(sharedLoading, fonts.EnsureLoadedAsync());
            Assert.Null(context.Dialogs.ConversionReview);
            Assert.Same(original, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            Assert.Equal(AegiTaskState.Cancelled, ImportSnapshot(application.Tasks).State);
        }
        finally
        {
            pending.TrySetResult(SystemFontCatalog.Empty);
            await sharedLoading.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ProjectChangeWhileCatalogIsPendingRejectsThePreparedImport()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var pending = new TaskCompletionSource<SystemFontCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fonts = new SubtitleFontSelectionService(() => pending.Task);
        await using var application = CreateApplication(directory.Path, fonts);
        await application.Initialization;
        await using var context = new WorkspaceSessionTestContext(applicationContext: application);
        await PrepareImportAsync(context);
        context.Dialogs.ConversionChoice = true;
        var original = context.Editor.Snapshot;
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var operation = workflow.ImportSubtitlesAsync(true);
        try
        {
            await WaitForRunningImportAsync(application.Tasks);
            context.Editor.Apply("change during font loading", document => document with { Name = "Changed" });
            var changed = context.Editor.Snapshot;
            pending.SetResult(SystemFontCatalog.Empty);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Same(changed, context.Editor.Snapshot);
            Assert.Empty(context.Editor.Snapshot.Subtitles);
            Assert.True(context.Editor.Undo());
            Assert.Same(original, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
        }
        finally
        {
            pending.TrySetResult(SystemFontCatalog.Empty);
        }
    }

    private static DesktopApplicationContext CreateApplication(string directory, SubtitleFontSelectionService fonts) =>
        new(new WorkbenchPreferencesStore(directory),
            new() { Projects = new() { WorkspaceRoot = Path.Combine(directory, "workspace") } }, fonts);

    private static async Task PrepareImportAsync(WorkspaceSessionTestContext context)
    {
        await context.InitializeAsync();
        var path = Path.Combine(context.DirectoryPath, "weight.ass");
        await File.WriteAllTextAsync(path, "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\fnFixture\\b600}x\n");
        context.Dialogs.OpenPath = path;
    }

    private static AegiTaskSnapshot ImportSnapshot(AegiTaskService tasks) =>
        Assert.Single(tasks.GetSnapshots(), snapshot => snapshot.Name == "Tasks.ImportSubtitles");

    private static async Task<AegiTaskSnapshot> WaitForRunningImportAsync(AegiTaskService tasks)
    {
        var running = new TaskCompletionSource<AegiTaskSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs args)
        {
            var snapshot = tasks.GetSnapshots().FirstOrDefault(item => item.Name == "Tasks.ImportSubtitles" && item.State == AegiTaskState.Running);
            if (snapshot is not null)
            {
                running.TrySetResult(snapshot);
            }
        }
        tasks.Changed += Check;
        try
        {
            Check(null, EventArgs.Empty);
            return await running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            tasks.Changed -= Check;
        }
    }
}
