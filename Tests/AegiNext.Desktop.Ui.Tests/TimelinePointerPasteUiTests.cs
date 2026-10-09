using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelinePointerPasteUiTests
{
    [AvaloniaFact]
    public async Task CrossTrackContextPasteKeepsTheClickedTimeAndTrackAfterThePointerMoves()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        await context.Controller.SeekAsync(new(10));
        var target = PointAt(timeline, destination.Id, 6);
        var menu = OpenMenu(context, timeline, target);
        Assert.True(menu.IsOpen);
        var changed = WindowPoint(context, timeline, PointAt(timeline, original.Layers.Single(clip => clip.SubtitleId == source.Id).TrackId, 8));
        context.Window.MouseMove(changed);
        Flush(context.Window);
        Assert.True(menu.IsOpen);
        try
        {
            await ExecutePasteAsync(menu);
        }
        finally
        {
            menu.Close();
        }
        Flush(context.Window);

        AssertPasted(context, original, source, destination.Id, new(6));
    }

    [AvaloniaFact]
    public async Task HoverAndPasteUsesThePointerTrackAndTimeWithoutClickingOrChangingThePlayhead()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        await context.Controller.SeekAsync(new(10));
        Flush(context.Window);
        var playhead = timeline.Position;
        Hover(context, timeline, PointAt(timeline, destination.Id, 6));

        Assert.Equal(source.Id, context.Session.SelectedLayerId);
        Assert.Equal(original.Layers.Single(clip => clip.SubtitleId == source.Id).TrackId, context.Session.CurrentTrackId);
        Assert.Equal(playhead, timeline.Position);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.V, CommandModifier());
        Flush(context.Window);

        Assert.Equal(playhead, timeline.Position);
        AssertPasted(context, original, source, destination.Id, new(6));
    }

    [AvaloniaFact]
    public async Task PointerPasteResolvesTheCurrentTrackAndTimeAfterWheelScrollPanAndZoom()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var (fixture, source, destination) = ResetFixture(context);
        var extraTracks = Enumerable.Range(0, 10).Select(index => new ProjectTrack { Name = $"Extra {index}" }).ToArray();
        var original = fixture with
        {
            Tracks = [.. fixture.Tracks, .. extraTracks],
            Layers = fixture.Layers.Add(new()
            {
                TrackId = extraTracks[^1].Id, Kind = LayerKind.SHAPE, Start = MediaTime.Zero, End = new(60),
                Shape = new(ShapeKind.RECTANGLE, 30, 40)
            })
        };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        var local = PointAt(timeline, destination.Id, 6);
        Hover(context, timeline, local);
        var point = WindowPoint(context, timeline, local);
        Assert.True(timeline.ContentHeight > timeline.Viewport.Height);
        context.Window.MouseWheel(point, new(0, -1));
        context.Window.MouseWheel(point, new(0, -1), RawInputModifiers.Shift);
        context.Window.MouseWheel(point, new(0, 1), CommandModifier());
        Flush(context.Window);

        Assert.Equal(36, timeline.Viewport.VerticalOffset, 6);
        Assert.True(timeline.ViewStart > 0);
        Assert.True(timeline.PixelsPerSecond > 60);
        var currentTrack = original.Tracks[2];
        Assert.True(timeline.GetTrackHeaderRectangle(currentTrack.Id)!.Value.Top <= local.Y);
        Assert.True(timeline.GetTrackHeaderRectangle(currentTrack.Id)!.Value.Bottom > local.Y);
        var currentTime = timeline.ViewStart + (local.X - timeline.HeaderWidth) / timeline.PixelsPerSecond;
        var expected = new MediaTime((long)Math.Round(currentTime * original.FrameRate.Numerator / original.FrameRate.Denominator),
            original.FrameRate.Numerator) * original.FrameRate.Denominator;
        Assert.NotEqual(new MediaTime(6), expected);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.V, CommandModifier());
        Flush(context.Window);

        AssertPasted(context, original, source, currentTrack.Id, expected);
    }

    [AvaloniaTheory]
    [InlineData("header")]
    [InlineData("ruler")]
    [InlineData("outside")]
    public async Task PasteDoesNothingAfterThePointerLeavesAValidTimelineBodyTarget(string invalidRegion)
    {
        await using var context = new MainWindowTestContext();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        var valid = PointAt(timeline, destination.Id, 6);
        Hover(context, timeline, valid);
        Assert.NotNull(timeline.GetClipPasteTarget());
        var invalid = invalidRegion switch
        {
            "header" => new Point(timeline.HeaderWidth / 2, valid.Y),
            "ruler" => new Point(valid.X, timeline.RulerHeight / 2),
            _ => new Point(timeline.Bounds.Width + 20, timeline.Bounds.Height + 20)
        };
        context.Window.MouseMove(WindowPoint(context, timeline, invalid));
        Flush(context.Window);
        Assert.Null(timeline.GetClipPasteTarget());
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.V, CommandModifier());
        Flush(context.Window);

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(source.Id, context.Session.SelectedLayerId);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task HoverPasteIntoAnotherTracksOccupiedIntervalIsRejectedWithoutAnUndo()
    {
        await using var context = new MainWindowTestContext();
        var (fixture, source, destination) = ResetFixture(context);
        var obstacle = new SubtitleLine { Start = new(6), End = new(8), Text = "Occupied" };
        var original = fixture with { Subtitles = [.. fixture.Subtitles, obstacle], Layers = [.. fixture.Layers, Layer(obstacle) with { TrackId = destination.Id }] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        Hover(context, timeline, PointAt(timeline, destination.Id, 7));
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.V, CommandModifier());
        Flush(context.Window);

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Equal(source.Id, context.Session.SelectedLayerId);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.NotNull(context.Session.LastError);
    }

    [AvaloniaFact]
    public async Task AValidPointerTargetDoesNotOverrideFocusedSubtitleTextInputPaste()
    {
        await using var context = new MainWindowTestContext();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        Hover(context, timeline, PointAt(timeline, destination.Id, 6));
        Assert.NotNull(timeline.GetClipPasteTarget());
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == source.Id && Grid.GetColumn(control) == 4);
        Assert.True(input.Focus());
        input.SelectAll();
        await context.Window.Clipboard!.SetTextAsync("Local text paste");
        try
        {
            UiTestActions.Press(context.Window, Key.V, CommandModifier());
            Flush(context.Window);
            Assert.True(input.IsFocused);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(source.Id, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Id);
        }
        finally
        {
            context.ViewModel.Subtitles.Rows.Single(row => row.Id == source.Id).Accept(source);
        }
    }

    [AvaloniaFact]
    public async Task OpenClipMenuBlocksFocusCommandsEvenWhenTheTimelineRetainsFocus()
    {
        await using var context = new MainWindowTestContext();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        var menu = OpenMenu(context, timeline, PointAt(timeline, destination.Id, 6));
        var panel = Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());
        try
        {
            Assert.True(menu.IsOpen);
            Assert.False(panel.CanExecuteFocusCommand(WorkbenchCommand.PASTE_CLIPS, timeline));
            Assert.False(panel.TryExecuteFocusCommand(WorkbenchCommand.PASTE_CLIPS, timeline));
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("drag")]
    [InlineData("detach")]
    [InlineData("dispose")]
    public async Task ActiveGestureAndReleasedControlsHaveNoPointerPasteTarget(string state)
    {
        await using var context = new MainWindowTestContext();
        var (original, source, destination) = ResetFixture(context);
        var timeline = Prepare(context);
        CopyThroughInput(context, timeline, source.Id);
        Hover(context, timeline, PointAt(timeline, destination.Id, 6));
        Assert.NotNull(timeline.GetClipPasteTarget());
        if (state == "drag")
        {
            var origin = WindowPoint(context, timeline, timeline.GetClipRectangle(source.Id)!.Value.Center);
            context.Window.MouseDown(origin, MouseButton.Left);
            context.Window.MouseMove(origin + new Vector(timeline.PixelsPerSecond, 0));
            Assert.True(timeline.HasActiveDrag);
            Assert.Null(timeline.GetClipPasteTarget());
            UiTestActions.Press(context.Window, Key.V, CommandModifier());
            timeline.CancelGesture();
            context.Window.MouseUp(origin, MouseButton.Left);
        }
        else if (state == "detach")
        {
            context.Window.Layouts.Hide("timeline");
            Flush(context.Window);
            Assert.Null(timeline.GetClipPasteTarget());
        }
        else
        {
            timeline.Dispose();
            Assert.Null(timeline.GetClipPasteTarget());
            Hover(context, timeline, PointAt(timeline, original.Layers.Single(clip => clip.SubtitleId == source.Id).TrackId, 8));
            Assert.Null(timeline.GetClipPasteTarget());
        }

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static (ProjectDocument Document, SubtitleLine Source, ProjectTrack Destination) ResetFixture(MainWindowTestContext context)
    {
        var source = new SubtitleLine { Start = new(1), End = new(3), Text = "Source subtitle" };
        var destination = new ProjectTrack { Name = "Paste destination" };
        var document = context.Session.DocumentSnapshot with
        {
            Tracks = [ProjectTrack.Default, destination], Subtitles = [source], Layers = [Layer(source)]
        };
        context.Session.Editor.Reset(document);
        return (document, source, destination);
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static SubtitleTimelineControl Prepare(MainWindowTestContext context)
    {
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        context.ViewModel.Timeline.Viewport = timeline.Viewport with { PixelsPerSecond = 60, StartSeconds = 0, VerticalOffset = 0 };
        Flush(context.Window);
        return timeline;
    }

    private static void CopyThroughInput(MainWindowTestContext context, SubtitleTimelineControl timeline, Guid id)
    {
        var point = WindowPoint(context, timeline, timeline.GetClipRectangle(id)!.Value.Center);
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.C, CommandModifier());
        Flush(context.Window);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static void AssertPasted(MainWindowTestContext context, ProjectDocument original, SubtitleLine source,
        Guid targetTrack, MediaTime start)
    {
        var copy = Assert.Single(context.Session.DocumentSnapshot.Subtitles, cue => cue.Id != source.Id);
        Assert.Equal(targetTrack, context.Session.ClipIndex.GetSubtitleTrackId(copy.Id));
        Assert.Equal(start, copy.Start);
        Assert.Equal(start + source.End - source.Start, copy.End);
        Assert.Equal(source.Text, copy.Text);
        Assert.Equal(source, context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == source.Id));
        var layer = Assert.Single(context.Session.DocumentSnapshot.Layers, value => value.SubtitleId == copy.Id);
        Assert.Equal(copy.Id, context.Session.SelectedCueId);
        Assert.Equal(layer.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static Point PointAt(SubtitleTimelineControl timeline, Guid trackId, double seconds)
    {
        var row = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        return new(timeline.HeaderWidth + (seconds - timeline.ViewStart) * timeline.PixelsPerSecond, row.Bottom - 14);
    }

    private static Point WindowPoint(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local) =>
        timeline.TranslatePoint(local, context.Window)!.Value;

    private static void Hover(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local)
    {
        var point = WindowPoint(context, timeline, local);
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseMove(point);
        Flush(context.Window);
    }

    private static ContextMenu OpenMenu(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local)
    {
        var point = WindowPoint(context, timeline, local);
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Right);
        context.Window.MouseUp(point, MouseButton.Right);
        Flush(context.Window);
        return Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>()).ClipMenu;
    }

    private static async Task ExecutePasteAsync(ContextMenu menu)
    {
        var item = TimelineTrackTestActions.Item(menu, "PasteTimelineClipsMenuItem");
        Assert.True(item.Command!.CanExecute(null));
        if (item.Command is IAsyncRelayCommand command)
        {
            await command.ExecuteAsync(null);
        }
        else
        {
            item.Command.Execute(null);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static RawInputModifiers CommandModifier() => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
