using System.Collections.Immutable;
using AegiNext.Application;
using AegiNext.Core.Media;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SelectedTimingPostProcessorWorkflowTests
{
    [Fact]
    public async Task ActualSelectedSubtitleClipsAcrossTracksAreProcessedWhenThePrimaryClipIsAShape()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var firstPreset = Preset(LeadOptions(100, 200));
        var secondPreset = Preset(LeadOptions(300, 450));
        var secondTrack = new SubtitleTrack { Name = "Other track" };
        var first = Cue(firstPreset, 2, 3);
        var second = Cue(secondPreset, 2, 3) with { TrackId = secondTrack.Id };
        var unselected = Cue(firstPreset, 5, 6);
        var shape = Shape();
        var document = Document([first, second, unselected], [SubtitleTrack.Default, secondTrack], shape);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, firstPreset, secondPreset);
        var firstLayer = LayerFor(document, first);
        var secondLayer = LayerFor(document, second);
        Select(session, shape.Id, firstLayer.Id, secondLayer.Id, shape.Id);
        Assert.NotEqual(first.Id, firstLayer.Id);
        Assert.NotEqual(second.Id, secondLayer.Id);
        Assert.Empty(session.SelectedSubtitleIds);
        Assert.True(session.HasApplicableSelectedTimingPostProcessor);

        var changed = await session.ApplySelectedTimingPostProcessorAsync();

        Assert.Equal(2, changed);
        AssertTiming(editor.Snapshot, first.Id, new(1900, 1000), new(3200, 1000));
        AssertTiming(editor.Snapshot, second.Id, new(1700, 1000), new(3450, 1000));
        Assert.Same(unselected, editor.Snapshot.Subtitles.Single(line => line.Id == unselected.Id));
        Assert.Same(shape, editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id));
        Assert.Equal(shape.Id, session.SelectedLayerId);
        Assert.Equal(new[] { firstLayer.Id, secondLayer.Id, shape.Id }.Order(), session.TimelineClipIds().Order());
        var result = editor.Snapshot;
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result, editor.Snapshot);
        Assert.False(session.IsProjectBusy);
    }

    [Fact]
    public async Task EqualParametersOnDifferentPresetsConnectSelectedNeighborsInTheSameTrack()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var options = NoStages() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300, BiasPercent = 50 };
        var firstPreset = Preset(options);
        var secondPreset = Preset(options with { });
        var first = Cue(firstPreset, 1, 2);
        var second = Cue(secondPreset, 3, 4) with { Start = new(2200, 1000) };
        var document = Document([first, second]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, firstPreset, secondPreset);
        Select(session, LayerFor(document, first).Id, document.Layers.Select(layer => layer.Id).ToArray());

        Assert.Equal(2, await session.ApplySelectedTimingPostProcessorAsync());

        AssertTiming(editor.Snapshot, first.Id, new(1), new(2100, 1000));
        AssertTiming(editor.Snapshot, second.Id, new(2100, 1000), new(4));
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentParametersOrTracksKeepAdjacencyGroupsIndependent(bool crossTrack)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var options = NoStages() with { AdjacencyEnabled = true, MaximumGapMilliseconds = 300, BiasPercent = 50 };
        var firstPreset = Preset(options);
        var secondPreset = Preset(crossTrack ? options : options with { BiasPercent = 90 });
        var otherTrack = new SubtitleTrack { Name = "Independent" };
        var first = Cue(firstPreset, 1, 2);
        var second = Cue(secondPreset, 3, 4) with
        {
            Start = new(2200, 1000), TrackId = crossTrack ? otherTrack.Id : first.TrackId
        };
        var document = Document([first, second], crossTrack ? [SubtitleTrack.Default, otherTrack] : [SubtitleTrack.Default]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, firstPreset, secondPreset);
        Select(session, LayerFor(document, first).Id, document.Layers.Select(layer => layer.Id).ToArray());

        Assert.Equal(0, await session.ApplySelectedTimingPostProcessorAsync());

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingOrUnassociatedStableIdentityDoesNotFallBackToTheSameName(int state)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var associated = Preset(LeadOptions(100, 200));
        var selectedPreset = associated with { Id = Guid.NewGuid(), TimingPostProcessor = state == 1 ? null : associated.TimingPostProcessor };
        var cue = Cue(selectedPreset, 2, 3);
        var document = Document([cue]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, associated);
        if (state != 0)
        {
            await session.Styles.UpsertAsync(selectedPreset with { Name = $"Other {selectedPreset.Id:N}" });
        }
        if (state == 2)
        {
            await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.RemoveAsync(selectedPreset.Id));
        }
        Select(session, LayerFor(document, cue).Id);

        Assert.False(session.HasApplicableSelectedTimingPostProcessor);
        Assert.Equal(0, await session.ApplySelectedTimingPostProcessorAsync());

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task RenamingAnAssociatedPresetKeepsItsExistingSubtitleIdentityUsable()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(LeadOptions(100, 200));
        var cue = Cue(preset, 2, 3);
        var document = Document([cue]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, preset);
        await session.Styles.UpsertAsync(preset with { Name = "Renamed 中文 ABC 123" });
        Select(session, LayerFor(document, cue).Id);

        Assert.True(session.HasApplicableSelectedTimingPostProcessor);
        Assert.Equal(1, await session.ApplySelectedTimingPostProcessorAsync());

        var result = Assert.Single(editor.Snapshot.Subtitles);
        Assert.Equal(preset.Id, result.StylePresetId);
        Assert.Equal(cue.StyleName, result.StyleName);
        AssertTiming(editor.Snapshot, cue.Id, new(1900, 1000), new(3200, 1000));
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyResolutionPersistsIdentityInOneUndoEvenWhenTimingDoesNotChange(bool trackIdentity)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(NoStages() with { LeadInEnabled = true, LeadInMilliseconds = 0 });
        var cue = Cue(preset, 2, 3) with { StylePresetId = null };
        var track = SubtitleTrack.Default with
        {
            DefaultStyle = trackIdentity ? preset.Style : null,
            StylePresetId = trackIdentity ? preset.Id : null,
            StylePresetName = trackIdentity ? preset.Name : null
        };
        var document = Document([cue], [track]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, preset);
        Select(session, LayerFor(document, cue).Id);

        Assert.True(session.HasApplicableSelectedTimingPostProcessor);
        Assert.Equal(0, await session.ApplySelectedTimingPostProcessorAsync());

        Assert.Equal(preset.Id, Assert.Single(editor.Snapshot.Subtitles).StylePresetId);
        AssertTiming(editor.Snapshot, cue.Id, cue.Start, cue.End);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task InvalidSubtitleDraftIsPreservedAndPreventsTheSelectedBatch()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(LeadOptions(100, 200));
        var cue = Cue(preset, 2, 3);
        var document = Document([cue]);
        var editor = new ProjectEditor(document);
        await using var session = CreateSession(directory.Path, editor);
        await SeedAsync(session, preset);
        Select(session, LayerFor(document, cue).Id);
        var row = session.ViewModel.Subtitles.Rows.Single(value => value.Id == cue.Id);
        row.StartText = "invalid";
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplySelectedTimingPostProcessorAsync());

            Assert.Equal("invalid", row.StartText);
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsProjectBusy);
        }
        finally
        {
            row.Accept(cue);
        }
    }

    [Fact]
    public async Task PendingVideoProbeFreezesActualClipSelectionAndRejectsASecondBatch()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(LeadOptions(100, 200) with { KeyframeSnapEnabled = true });
        var first = Cue(preset, 2, 3);
        var second = Cue(preset, 4, 5);
        var document = WithMedia(Document([first, second]), Path.Combine(directory.Path, "source.mkv"));
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await pending.Task.WaitAsync(token);
        });
        await SeedAsync(session, preset);
        await session.Controller.OpenAsync(document.Assets[0].ExternalPath!);
        var firstLayer = LayerFor(document, first);
        Select(session, firstLayer.Id);
        var operation = session.ApplySelectedTimingPostProcessorAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.IsProjectBusy);
            Assert.False(session.SelectTimelineLayers(new(LayerFor(document, second).Id, [LayerFor(document, second).Id])));
            Assert.Equal(firstLayer.Id, session.SelectedLayerId);
            Assert.Equal(new[] { firstLayer.Id }, session.TimelineClipIds());
            Assert.Equal(0, await session.ApplySelectedTimingPostProcessorAsync());
            pending.TrySetResult(VideoIndex());

            Assert.Equal(1, await operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(second, editor.Snapshot.Subtitles.Single(value => value.Id == second.Id));
            Assert.False(session.IsProjectBusy);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            pending.TrySetResult(VideoIndex());
            await ObserveCancelledAsync(operation);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrSessionReleaseDuringTheSelectedProbeDoesNotSubmitAResult(bool releaseSession)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(LeadOptions(100, 200) with { KeyframeSnapEnabled = true });
        var cue = Cue(preset, 2, 3);
        var document = WithMedia(Document([cue]), Path.Combine(directory.Path, "source.mkv"));
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await pending.Task.WaitAsync(token);
        });
        await SeedAsync(session, preset);
        await session.Controller.OpenAsync(document.Assets[0].ExternalPath!);
        Select(session, LayerFor(document, cue).Id);
        var operation = session.ApplySelectedTimingPostProcessorAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (releaseSession)
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                cancellation.Cancel();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(session.IsProjectBusy);
        }
        finally
        {
            cancellation.Cancel();
            pending.TrySetResult(VideoIndex());
            await ObserveCancelledAsync(operation);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentOrMediaSupersessionWhileTheSelectedProbeIsPendingRejectsItsResult(bool replaceMedia)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var preset = Preset(LeadOptions(100, 200) with { KeyframeSnapEnabled = true });
        var cue = Cue(preset, 2, 3);
        var document = WithMedia(Document([cue]), Path.Combine(directory.Path, "source.mkv"));
        var editor = new ProjectEditor(document);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<VideoTimingIndex>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = CreateSession(directory.Path, editor, async (_, _, _, token) =>
        {
            entered.TrySetResult();
            return await pending.Task.WaitAsync(token);
        });
        await SeedAsync(session, preset);
        await session.Controller.OpenAsync(document.Assets[0].ExternalPath!);
        Select(session, LayerFor(document, cue).Id);
        var operation = session.ApplySelectedTimingPostProcessorAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (replaceMedia)
            {
                await session.Controller.OpenAsync(Path.Combine(directory.Path, "replacement.mkv"));
            }
            else
            {
                editor.UpdateSubtitle(cue.Id, value => value with { Text = "External edit while indexing" });
            }
            var latest = editor.Snapshot;
            pending.TrySetResult(VideoIndex());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(latest, editor.Snapshot);
            AssertTiming(editor.Snapshot, cue.Id, cue.Start, cue.End);
            Assert.False(session.IsProjectBusy);
            if (!replaceMedia)
            {
                Assert.True(editor.Undo());
                Assert.Same(document, editor.Snapshot);
            }
            Assert.False(editor.CanUndo);
        }
        finally
        {
            pending.TrySetResult(VideoIndex());
            await ObserveCancelledAsync(operation);
        }
    }

    private static SubtitleStylePreset Preset(TimingPostProcessorOptions options) =>
        new(Guid.NewGuid(), $"Timing {Guid.NewGuid():N}", new(), TimingPostProcessor: options);

    private static SubtitleLine Cue(SubtitleStylePreset preset, int start, int end) => new()
    {
        Start = new(start), End = new(end), Text = "Selected 中文 ABC 123", StyleName = preset.Name, StylePresetId = preset.Id
    };

    private static TimingPostProcessorOptions LeadOptions(int leadIn, int leadOut) => NoStages() with
    {
        LeadInEnabled = true, LeadInMilliseconds = leadIn, LeadOutEnabled = true, LeadOutMilliseconds = leadOut
    };

    private static TimingPostProcessorOptions NoStages() => new()
    {
        LeadInEnabled = false, LeadOutEnabled = false, AdjacencyEnabled = false, KeyframeSnapEnabled = false
    };

    private static ProjectLayer Shape() => new()
    {
        Name = "Primary shape", Kind = LayerKind.SHAPE, Start = new(1), End = new(7), Shape = new(ShapeKind.RECTANGLE, 80, 50)
    };

    private static ProjectDocument Document(ImmutableArray<SubtitleLine> lines,
        ImmutableArray<SubtitleTrack> tracks = default, ProjectLayer? shape = null) => new()
    {
        SubtitleTracks = tracks.IsDefault ? [SubtitleTrack.Default] : tracks,
        Subtitles = lines,
        Layers = [.. lines.Select(line => new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
        }), .. (shape is null ? ImmutableArray<ProjectLayer>.Empty : [shape])]
    };

    private static ProjectLayer LayerFor(ProjectDocument document, SubtitleLine line) =>
        document.Layers.Single(layer => layer.SubtitleId == line.Id);

    private static void Select(WorkbenchSession session, Guid primary, params Guid[] selection)
    {
        Assert.True(session.SelectTimelineLayers(new(primary, selection.Length == 0 ? [primary] : selection)));
    }

    private static void AssertTiming(ProjectDocument document, Guid cueId, MediaTime start, MediaTime end)
    {
        var cue = document.Subtitles.Single(value => value.Id == cueId);
        var layer = document.Layers.Single(value => value.SubtitleId == cueId);
        Assert.Equal(start, cue.Start);
        Assert.Equal(end, cue.End);
        Assert.Equal(start, layer.Start);
        Assert.Equal(end, layer.End);
    }

    private static async Task SeedAsync(WorkbenchSession session, params SubtitleStylePreset[] presets)
    {
        await session.Styles.Completion;
        foreach (var preset in presets)
        {
            await session.Styles.UpsertAsync(preset);
        }
    }

    private static WorkbenchSession CreateSession(string directory, ProjectEditor editor,
        Func<string, int, MediaTime, CancellationToken, Task<VideoTimingIndex>>? probe = null) =>
        new(new WorkspaceDialogStub(), _ => new VideoPreviewController((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(7)));
        }, (_, _) => new(_ => new PreviewTestSource(7, 0, 100, 200)), () => new PreviewTestConverter(), Dispatch, static _ => { }),
            Dispatch, editor, new WorkbenchPreferencesStore(directory), videoTimingProbe: probe);

    private static ProjectDocument WithMedia(ProjectDocument document, string path)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: path);
        return document with { Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero) };
    }

    private static VideoTimingIndex VideoIndex() => new(
        [new(0), new(1), new(2), new(3), new(4), new(5), new(6)], [0, 2, 3, 4, 5, 6]);

    private static async Task ObserveCancelledAsync(Task<int> operation)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
