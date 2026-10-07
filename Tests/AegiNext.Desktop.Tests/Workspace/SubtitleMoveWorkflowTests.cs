using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleMoveWorkflowTests
{
    [Theory]
    [InlineData(false, 125)]
    [InlineData(false, -125)]
    [InlineData(true, 125)]
    [InlineData(true, -125)]
    public async Task ConfirmedMillisecondsMoveTheWholeSelectionWithOneUndo(bool timeline, int milliseconds)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        context.Dialogs.IntegerInputChoice = milliseconds;

        await MoveAsync(context.Session, targets, timeline, original);

        Assert.Equal(1, context.Dialogs.IntegerInputRequests);
        Assert.NotNull(context.Dialogs.LastIntegerInputRequest);
        Assert.Null(context.Session.LastError);
        var offset = new MediaTime(milliseconds, 1000);
        var moved = context.Editor.Snapshot;
        foreach (var cue in original.Subtitles.Take(2))
        {
            Assert.Equal(cue with { Start = cue.Start + offset, End = cue.End + offset },
                moved.Subtitles.Single(value => value.Id == cue.Id));
        }
        var layerIds = original.Layers.Take(2).Select(layer => layer.Id).ToHashSet();
        if (timeline)
        {
            layerIds.Add(original.Layers[^1].Id);
        }
        foreach (var layer in original.Layers)
        {
            var actual = moved.Layers.Single(value => value.Id == layer.Id);
            if (layerIds.Contains(layer.Id))
            {
                Assert.Equal(layer with { Start = layer.Start + offset, End = layer.End + offset }, actual);
            }
            else
            {
                Assert.Same(layer, actual);
            }
        }
        Assert.Same(original.Subtitles[2], moved.Subtitles[2]);
        AssertSelection(context.Session, original, timeline);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(moved, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, 0)]
    [InlineData(true, null)]
    [InlineData(true, 0)]
    public async Task CancelAndZeroPreserveUncommittedDraftSelectionAndHistory(bool timeline, int? choice)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        var row = context.Session.ViewModel.Subtitles.Rows.Single(value => value.Id == original.Subtitles[0].Id);
        row.Text = "Uncommitted draft";
        context.Dialogs.IntegerInputChoice = choice;

        await MoveAsync(context.Session, targets, timeline, original);

        Assert.Equal(1, context.Dialogs.IntegerInputRequests);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(row.IsDirty);
        Assert.Equal("Uncommitted draft", row.Text);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
        Assert.Null(context.Session.LastError);
        AssertSelection(context.Session, original, timeline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSubtitleDraftPreventsTheConfirmedBatchMove(bool timeline)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        var row = context.Session.ViewModel.Subtitles.Rows.Single(value => value.Id == original.Subtitles[0].Id);
        row.StartText = "invalid";
        context.Dialogs.IntegerInputChoice = 125;
        try
        {
            await MoveAsync(context.Session, targets, timeline, original);

            Assert.Same(original, context.Editor.Snapshot);
            Assert.Equal("invalid", row.StartText);
            Assert.True(row.IsDirty);
            Assert.False(context.Editor.CanUndo);
            Assert.NotNull(context.Session.ViewModel.InvalidFieldKey);
            AssertSelection(context.Session, original, timeline);
        }
        finally
        {
            row.Accept(original.Subtitles[0]);
        }
    }

    [Theory]
    [InlineData(false, -1001)]
    [InlineData(false, 3000)]
    [InlineData(true, -1001)]
    [InlineData(true, 3000)]
    public async Task CrossingZeroOrCollidingRejectsEverySelectedMember(bool timeline, int milliseconds)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        context.Dialogs.IntegerInputChoice = milliseconds;

        await MoveAsync(context.Session, targets, timeline, original);

        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
        Assert.NotNull(context.Session.LastError);
        AssertSelection(context.Session, original, timeline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleMenuSnapshotCannotOpenAnInputOrMoveClips(bool timeline)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        context.Dialogs.IntegerInputChoice = 125;
        context.Editor.Apply("Change project", document => document with { Name = "Changed" });
        var changed = context.Editor.Snapshot;

        await MoveAsync(context.Session, targets, timeline, original);

        Assert.Equal(0, context.Dialogs.IntegerInputRequests);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedSnapshotInvalidatesPendingInputWithoutApplyingTheOffset(bool timeline)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        var decision = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.PendingIntegerInput = decision;
        var operation = MoveAsync(context.Session, targets, timeline, original);
        try
        {
            Assert.Equal(1, context.Dialogs.IntegerInputRequests);
            Assert.False(operation.IsCompleted);
            context.Editor.Apply("Change project", document => document with { Name = "Changed" });
            var changed = context.Editor.Snapshot;
            decision.SetResult(125);
            await operation;

            Assert.Same(changed, context.Editor.Snapshot);
            Assert.Null(context.Session.LastError);
            AssertSelection(context.Session, original, timeline);
            Assert.True(context.Editor.Undo());
            Assert.Same(original, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
        }
        finally
        {
            decision.TrySetResult(null);
            await operation;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionDisposalCancelsPendingInputWithoutWaitingForAUserDecision(bool timeline)
    {
        var original = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(original);
        await context.InitializeAsync();
        var targets = SelectTargets(context.Session, original, timeline);
        var decision = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.PendingIntegerInput = decision;
        var operation = MoveAsync(context.Session, targets, timeline, original);
        try
        {
            Assert.Equal(1, context.Dialogs.IntegerInputRequests);
            Assert.False(operation.IsCompleted);

            await context.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(decision.Task.IsCompleted);
            Assert.Same(original, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            Assert.Null(context.Session.LastError);
        }
        finally
        {
            decision.TrySetResult(null);
            await operation;
        }
    }

    private static Task MoveAsync(WorkbenchSession session, Guid[] ids, bool timeline, ProjectDocument expected)
    {
        return timeline ? session.MoveTimelineClipsAsync(ids, expected) : session.MoveSubtitleSelectionAsync(ids, expected);
    }

    private static Guid[] SelectTargets(WorkbenchSession session, ProjectDocument document, bool timeline)
    {
        if (timeline)
        {
            var layers = new[] { document.Layers[0].Id, document.Layers[1].Id, document.Layers[^1].Id };
            session.SelectLayer(layers[1], layers);
            return layers;
        }

        var subtitles = document.Subtitles.Take(2).Select(cue => cue.Id).ToArray();
        Assert.True(session.SelectSubtitleRows(subtitles[1], subtitles));
        return subtitles;
    }

    private static void AssertSelection(WorkbenchSession session, ProjectDocument document, bool timeline)
    {
        Assert.Equal(document.Subtitles.Take(2).Select(cue => cue.Id).Order(), session.SelectedSubtitleIds.Order());
        var layers = document.Layers.Take(2).Select(layer => layer.Id).ToList();
        if (timeline)
        {
            layers.Add(document.Layers[^1].Id);
        }
        Assert.Equal(layers.Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(document.Subtitles[1].Id, session.SelectedCueId);
        Assert.Equal(document.Layers[1].Id, session.SelectedLayerId);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var untouched = new SubtitleLine { Start = new(6), End = new(7), Text = "Untouched" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Start = new(3, 2), End = new(5, 2),
            Shape = new(ShapeKind.RECTANGLE, 30, 40), AnimationOffset = new(1, 3)
        };
        return new()
        {
            Subtitles = [first, second, untouched],
            Layers = [SubtitleLayer(first), SubtitleLayer(second), SubtitleLayer(untouched), shape]
        };
    }

    private static ProjectLayer SubtitleLayer(SubtitleLine cue)
    {
        return new()
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0.4)])]
        };
    }
}
