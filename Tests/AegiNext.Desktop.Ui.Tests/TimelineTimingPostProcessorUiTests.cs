using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTimingPostProcessorUiTests
{
    [AvaloniaFact]
    public async Task PointerSelectedLegacyDefaultClipInheritsItsMatchingAutoAppliedTrackAssociation()
    {
        await using var context = new MainWindowTestContext();
        var preset = Preset(100, 200) with { Name = "nano", Style = new() { FontSize = 42 } };
        await SeedAsync(context.Session, preset);
        Assert.DoesNotContain(context.Session.StyleLibrary.Snapshot.Presets, value => value.Name == "Default");
        var track = ProjectTrack.Default with
        {
            DefaultStyle = preset.Style, StylePresetId = preset.Id, StylePresetName = preset.Name, AutoApplyStyle = true
        };
        var first = new SubtitleLine
        {
            Start = new(2), End = new(3), Text = "Legacy selected 中文 ABC 123", Style = preset.Style
        };
        var second = first with { Id = Guid.NewGuid(), Start = new(5), End = new(6), Text = "Legacy unselected" };
        var json = JsonNode.Parse(ProjectStore.Serialize(Document([first, second], [track])))!.AsObject();
        foreach (var line in json["subtitles"]!.AsArray().OfType<JsonObject>())
        {
            line.Remove("stylePresetId");
        }
        var document = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var originalFirst = document.Subtitles.Single(value => value.Id == first.Id);
        var originalSecond = document.Subtitles.Single(value => value.Id == second.Id);
        Assert.Equal("Default", originalFirst.StyleName);
        Assert.Null(originalFirst.StylePresetId);
        Assert.Equal(document.Tracks[0].DefaultStyle, originalFirst.Style);
        Assert.NotSame(document.Tracks[0].DefaultStyle, originalFirst.Style);
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context.Window, context.Session);
        Select(context.Window, timeline, LayerFor(document, originalFirst).Id);
        Assert.Equal(new[] { LayerFor(document, originalFirst).Id }, context.ViewModel.Timeline.SelectedLayerIds);
        var button = UiTestActions.Find<Button>(context.Window, "TimelineTimingPostProcessorButton");
        Assert.True(button.IsEffectivelyEnabled);
        var command = Assert.IsType<AsyncRelayCommand>(button.Command);

        UiTestActions.Click(context.Window, "TimelineTimingPostProcessorButton");
        Assert.NotNull(command.ExecutionTask);
        await command.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
        Flush(context.Window);

        AssertTiming(context.Session.DocumentSnapshot, first.Id, new(1900, 1000), new(3200, 1000));
        Assert.Equal(preset.Id, context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == first.Id).StylePresetId);
        Assert.Same(originalSecond, context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == second.Id));
        Assert.Same(LayerFor(document, originalSecond), LayerFor(context.Session.DocumentSnapshot, originalSecond));
        Assert.Null(context.Session.LastError);
        Assert.False(context.Session.IsProjectBusy);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task PointerSelectedClipsAndBottomButtonUseEachAssociationAndOneUndoAcrossTracks()
    {
        await using var context = new MainWindowTestContext();
        var firstPreset = Preset(100, 200);
        var secondPreset = Preset(300, 450);
        await SeedAsync(context.Session, firstPreset, secondPreset);
        var otherTrack = new ProjectTrack { Name = "Other track" };
        var first = Cue(firstPreset, 2, 3);
        var second = Cue(secondPreset, 2, 3);
        var unselected = Cue(firstPreset, 5, 6);
        var shapeTrack = new ProjectTrack { Name = "Shape track" };
        var shape = new ProjectLayer
        {
            TrackId = shapeTrack.Id, Name = "Primary shape", Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 80, 50),
            Start = new(1), End = new(7)
        };
        var document = Document([first, second, unselected], [ProjectTrack.Default, otherTrack, shapeTrack], shape);
        document = document with
        {
            Layers = document.Layers.Select(clip => clip.SubtitleId == second.Id ? clip with { TrackId = otherTrack.Id } : clip).ToImmutableArray()
        };
        context.Session.Editor.Reset(document);
        using var panel = new TimelinePanelView(context.ViewModel.Timeline, context.Session);
        var window = new Window { Width = 720, Height = 360, Content = panel };
        window.Show();
        try
        {
            var timeline = Prepare(window, context.Session);
            var firstLayer = LayerFor(document, first);
            var secondLayer = LayerFor(document, second);
            Select(window, timeline, firstLayer.Id);
            Select(window, timeline, secondLayer.Id, ToggleModifier());
            Select(window, timeline, shape.Id, ToggleModifier());
            Assert.Equal(shape.Id, context.Session.SelectedLayerId);
            Assert.Empty(context.Session.SelectedSubtitleIds);
            var selected = new[] { firstLayer.Id, secondLayer.Id, shape.Id };
            Assert.Equal(selected.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
            var button = UiTestActions.Find<Button>(window, "TimelineTimingPostProcessorButton");
            var command = Assert.IsType<AsyncRelayCommand>(context.ViewModel.GetCommand(WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR));
            Assert.Same(command, button.Command);
            Assert.Same(command, context.ViewModel.Timeline.ApplyTimingPostProcessorCommand);
            Assert.True(button.IsEffectivelyEnabled);

            UiTestActions.Click(window, "TimelineTimingPostProcessorButton");
            Assert.NotNull(command.ExecutionTask);
            await command.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Flush(window);

            AssertTiming(context.Session.DocumentSnapshot, first.Id, new(1900, 1000), new(3200, 1000));
            AssertTiming(context.Session.DocumentSnapshot, second.Id, new(1700, 1000), new(3450, 1000));
            Assert.Same(unselected, context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == unselected.Id));
            Assert.Same(shape, context.Session.DocumentSnapshot.Layers.Single(value => value.Id == shape.Id));
            Assert.Equal(selected.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
            Assert.Null(context.Session.LastError);
            Assert.False(context.Session.IsProjectBusy);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AssociationChangesRefreshTheExistingButtonAndDeletedIdentityDoesNotMatchANewSameNamePreset()
    {
        await using var context = new MainWindowTestContext();
        var associated = Preset(100, 200);
        var preset = associated with { TimingPostProcessor = null };
        await SeedAsync(context.Session, preset);
        var cue = Cue(preset, 2, 3);
        var document = Document([cue]);
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context.Window, context.Session);
        Select(context.Window, timeline, LayerFor(document, cue).Id);
        var button = UiTestActions.Find<Button>(context.Window, "TimelineTimingPostProcessorButton");
        Assert.False(button.IsEffectivelyEnabled);

        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.SetTimingPostProcessorAsync(
            [preset.Id], associated.TimingPostProcessor));
        Flush(context.Window);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);

        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.SetTimingPostProcessorAsync(
            [preset.Id], null));
        Flush(context.Window);
        Assert.False(button.IsEffectivelyEnabled);

        await context.Session.ApplicationContext.RunStyleOperationAsync(() => context.Session.StyleLibrary.RemoveAsync(preset.Id));
        await context.Session.Styles.UpsertAsync(associated with { Id = Guid.NewGuid() });
        Flush(context.Window);
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task FloatingTimelineKeepsTheSameSharedCommandAndButtonSelectionScope()
    {
        await using var context = new MainWindowTestContext();
        var preset = Preset(100, 200);
        await SeedAsync(context.Session, preset);
        var cue = Cue(preset, 2, 3);
        var unselected = Cue(preset, 5, 6);
        var document = Document([cue, unselected]);
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context.Window, context.Session);
        Select(context.Window, timeline, LayerFor(document, cue).Id);
        var originalButton = UiTestActions.Find<Button>(context.Window, "TimelineTimingPostProcessorButton");
        context.Window.Layouts.Float("timeline");
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            Flush(floating);
            var button = UiTestActions.Find<Button>(floating, "TimelineTimingPostProcessorButton");
            Assert.Same(originalButton, button);
            var command = Assert.IsType<AsyncRelayCommand>(button.Command);
            Assert.Same(context.ViewModel.GetCommand(WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR), command);

            UiTestActions.Click(floating, "TimelineTimingPostProcessorButton");
            Assert.NotNull(command.ExecutionTask);
            await command.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));

            AssertTiming(context.Session.DocumentSnapshot, cue.Id, new(1900, 1000), new(3200, 1000));
            Assert.Same(unselected, context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == unselected.Id));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(100, true, 1d)]
    [InlineData(100, false, 2d)]
    [InlineData(120, true, 1.5d)]
    [InlineData(120, false, 1d)]
    [InlineData(180, true, 2d)]
    [InlineData(180, false, 1.5d)]
    [InlineData(300, true, 1d)]
    [InlineData(300, false, 2d)]
    public async Task FooterActionsStayVisibleHitTestableAndUnchangedWhenTheUpperToolbarScrolls(
        int height, bool dark, double scaling)
    {
        await using var context = new MainWindowTestContext();
        Localization.SetLanguage(dark ? "zh-CN" : "en-US");
        var preset = Preset(100, 200);
        await SeedAsync(context.Session, preset);
        var cue = Cue(preset, 2, 3);
        var document = Document([cue]);
        context.Session.Editor.Reset(document);
        context.ViewModel.Timeline.SelectLayer(LayerFor(document, cue).Id);
        using var panel = new TimelinePanelView(context.ViewModel.Timeline, context.Session);
        var window = new Window
        {
            Width = 640, Height = height, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            window.SetRenderScaling(scaling);
            Flush(window);
            Assert.Equal(scaling, window.RenderScaling);
            Assert.Equal(height, panel.Bounds.Height);
            var classic = UiTestActions.Find<ToolbarToggleButton>(window, "TimelineClassicTimingButton");
            var processor = UiTestActions.Find<Button>(window, "TimelineTimingPostProcessorButton");
            var scroller = UiTestActions.Find<ScrollViewer>(window, "TimelineToolbarScroller");
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline");
            var classicPosition = classic.TranslatePoint(default, panel)!.Value;
            var processorPosition = processor.TranslatePoint(default, panel)!.Value;
            var timelinePosition = timeline.TranslatePoint(default, panel)!.Value;
            foreach (var button in new Button[] { classic, processor })
            {
                Assert.Equal(32, button.Bounds.Width);
                Assert.Equal(32, button.Bounds.Height);
                var position = button.TranslatePoint(default, panel)!.Value;
                Assert.InRange(position.Y, 0, panel.Bounds.Height - button.Bounds.Height);
                Assert.True(position.X + button.Bounds.Width <= timelinePosition.X);
                AssertHit(window, button);
            }
            Assert.Equal(classicPosition.Y + classic.Bounds.Height + 4, processorPosition.Y);
            Assert.InRange(panel.Bounds.Height - processorPosition.Y - processor.Bounds.Height, 0, 8);
            Assert.True(processor.IsEffectivelyEnabled);
            Assert.Equal(Localization.Get("Workbench.TimelineTimingPostProcessor"), ToolTip.GetTip(processor));
            Assert.Equal(Localization.Get("Workbench.TimelineTimingPostProcessor"), AutomationProperties.GetName(processor));
            if (height < 300)
            {
                Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
                var point = scroller.TranslatePoint(new(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2), window)!.Value;
                window.MouseWheel(point, new(0, -10));
                Flush(window);
                Assert.True(scroller.Offset.Y > 0);
            }
            Assert.Equal(classicPosition, classic.TranslatePoint(default, panel)!.Value);
            Assert.Equal(processorPosition, processor.TranslatePoint(default, panel)!.Value);
            AssertHit(window, processor);
            SaveCapture(window, $"timeline-toolbar-{height}-{(dark ? "dark" : "light")}-{scaling:F1}x.png");

            UiTestActions.Click(window, "TimelineTimingPostProcessorButton");
            var command = Assert.IsType<AsyncRelayCommand>(processor.Command);
            Assert.NotNull(command.ExecutionTask);
            await command.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
            AssertTiming(context.Session.DocumentSnapshot, cue.Id, new(1900, 1000), new(3200, 1000));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    private static SubtitleStylePreset Preset(int leadIn, int leadOut) => new(Guid.NewGuid(), $"UI timing {Guid.NewGuid():N}", new(),
        TimingPostProcessor: new()
        {
            LeadInEnabled = true, LeadInMilliseconds = leadIn, LeadOutEnabled = true, LeadOutMilliseconds = leadOut,
            AdjacencyEnabled = false, KeyframeSnapEnabled = false
        });

    private static SubtitleLine Cue(SubtitleStylePreset preset, int start, int end) => new()
    {
        Start = new(start), End = new(end), Text = "Selected 中文 ABC 123", StyleName = preset.Name, StylePresetId = preset.Id
    };

    private static ProjectDocument Document(ImmutableArray<SubtitleLine> lines,
        ImmutableArray<ProjectTrack> tracks = default, ProjectLayer? shape = null) => new()
    {
        Tracks = tracks.IsDefault ? [ProjectTrack.Default] : tracks,
        Subtitles = lines,
        Layers = [.. lines.Select(line => new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
        }), .. (shape is null ? ImmutableArray<ProjectLayer>.Empty : [shape])]
    };

    private static ProjectLayer LayerFor(ProjectDocument document, SubtitleLine line) =>
        document.Layers.Single(layer => layer.SubtitleId == line.Id);

    private static async Task SeedAsync(WorkbenchSession session, params SubtitleStylePreset[] presets)
    {
        await session.Styles.Completion;
        foreach (var preset in presets)
        {
            await session.Styles.UpsertAsync(preset);
        }
    }

    private static SubtitleTimelineControl Prepare(Window window, WorkbenchSession session)
    {
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline");
        session.ViewModel.Timeline.IsSnapEnabled = false;
        session.ViewModel.Timeline.IsStepEnabled = false;
        session.ViewModel.Timeline.IsClassicTimingEnabled = false;
        timeline.PixelsPerSecond = 60;
        timeline.ViewStart = 0;
        Flush(window);
        return timeline;
    }

    private static void Select(Window window, SubtitleTimelineControl timeline, Guid id,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var rectangle = timeline.GetClipRectangle(id)!.Value;
        if (rectangle.Center.Y >= timeline.Bounds.Height - 4)
        {
            timeline.SetViewport(timeline.Viewport with
            {
                VerticalOffset = timeline.Viewport.VerticalOffset + rectangle.Center.Y - timeline.Bounds.Height + 24
            }, 20);
            Flush(window);
            rectangle = timeline.GetClipRectangle(id)!.Value;
        }
        var point = timeline.TranslatePoint(rectangle.Center, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(window);
        Assert.False(timeline.HasActiveDrag);
    }

    private static void AssertTiming(ProjectDocument document, Guid id, MediaTime start, MediaTime end)
    {
        var cue = document.Subtitles.Single(value => value.Id == id);
        var layer = document.Layers.Single(value => value.SubtitleId == id);
        Assert.Equal(start, cue.Start);
        Assert.Equal(end, cue.End);
        Assert.Equal(start, layer.Start);
        Assert.Equal(end, layer.End);
    }

    private static void AssertHit(Window window, Button button)
    {
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point);
        Assert.True(ReferenceEquals(hit, button) || hit is Visual visual && visual.GetVisualAncestors().Contains(button),
            $"{button.Name} must receive pointer input at its visible center; actual target: {hit}.");
    }

    private static void SaveCapture(Window window, string name)
    {
        Flush(window);
        var root = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, "src", "AegiNext.Desktop")))
        {
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Cannot locate workspace for capture.");
        }
        var directory = Path.Combine(root, "artifacts", "verification", "timing-post-processor", "ui", "timeline-manual");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static RawInputModifiers ToggleModifier() => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
