using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTracksAndNavigationUiTests
{
    [AvaloniaFact]
    public void SubtitleClipsCanCrossNeighboursAndCrossTrackCollisionsNeverCommit()
    {
        var secondTrack = new ProjectTrack { Name = "Second" };
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "moving" };
        var next = new SubtitleLine { Start = new(4), End = new(6), Text = "neighbour" };
        var obstacle = new SubtitleLine { End = new(2), Text = "target occupied" };
        var moving = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.25), new(new(2), 0.75)])]
        };
        var editor = new ProjectEditor(new()
        {
            Tracks = [ProjectTrack.Default, secondTrack], Subtitles = [first, next, obstacle],
            Layers = [moving, Layer(next), Layer(obstacle) with { TrackId = secondTrack.Id }]
        });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60 };
        timeline.SetDocument(editor.Snapshot, first.Id, moving);
        var commits = 0;
        timeline.ClipSelectionChanged += (_, e) =>
        {
            var selected = editor.Snapshot.Layers.Single(layer => layer.Id == e.Id);
            timeline.SetDocument(editor.Snapshot, selected.SubtitleId, selected, e.SelectedIds);
        };
        timeline.TimingChanged += (_, e) =>
        {
            commits++;
            editor.MoveSubtitleClip(e.SubtitleId!.Value, e.TrackId!.Value, e.Start, e.End, e.Mode, e.IsMove);
            timeline.SetDocument(editor.Snapshot, first.Id, editor.Snapshot.Layers.Single(layer => layer.Id == moving.Id));
        };
        var window = new Window { Width = 900, Height = 360, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var origin = timeline.GetClipRectangle(moving.Id)!.Value.Center;
            Assert.Same(timeline, window.InputHitTest(origin));
            Drag(window, origin, origin + new Vector(300, 0));
            Assert.Equal(new MediaTime(6), editor.Snapshot.Subtitles.Single(cue => cue.Id == first.Id).Start);
            Assert.Equal(new MediaTime(8), editor.Snapshot.Subtitles.Single(cue => cue.Id == first.Id).End);
            Assert.Equal(moving.Tracks, editor.Snapshot.Layers.Single(layer => layer.Id == moving.Id).Tracks);
            Assert.Equal(1, commits);

            Prepare(window);
            var before = editor.Snapshot;
            origin = timeline.GetClipRectangle(moving.Id)!.Value.Center;
            var targetY = timeline.GetTrackHeaderRectangle(secondTrack.Id)!.Value.Center.Y;
            Drag(window, origin, new(origin.X - 360, targetY));
            Assert.Same(before, editor.Snapshot);
            Assert.Equal(1, commits);

            origin = timeline.GetClipRectangle(moving.Id)!.Value.Center;
            Drag(window, origin, new(origin.X, targetY));
            var moved = editor.Snapshot.Subtitles.Single(cue => cue.Id == first.Id);
            Assert.Equal(secondTrack.Id, new ProjectClipIndex(editor.Snapshot).GetSubtitleTrackId(moved.Id));
            Assert.Equal(new MediaTime(6), moved.Start);
            Assert.Equal(new MediaTime(8), moved.End);
            var effects = editor.Snapshot.Layers.Single(layer => layer.SubtitleId == first.Id);
            Assert.Equal(moving.Id, effects.Id);
            Assert.Equal(moving.AnimationOffset, effects.AnimationOffset);
            Assert.Equal(moving.Tracks, effects.Tracks);
            Assert.Equal(2, commits);
            Prepare(window);
            var key = timeline.GetKeyframePoint(moving.Id, new(0), 0.25)!.Value;
            var rectangle = timeline.GetClipRectangle(moving.Id)!.Value;
            Assert.True(key.Y < rectangle.Top);
            Assert.True(key.Y > timeline.GetTrackHeaderRectangle(secondTrack.Id)!.Value.Top);
            Assert.Same(timeline, window.InputHitTest(key));
            Assert.True(editor.Undo());
            Assert.Same(before, editor.Snapshot);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TimelineMultiSelectionKeepsSubtitleIdentityAndTrackCollapseKeepsBothClipsVisible()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var firstCue = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(2), "First");
        context.Session.Editor.AddSubtitle(new(4), new(6), "Second");
        context.Session.SelectCue(firstCue);
        var originalIds = context.Window.DocumentSnapshot.Layers.Select(layer => layer.Id).ToArray();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Prepare(context.Window);
        var firstPoint = timeline.TranslatePoint(timeline.GetClipRectangle(originalIds[0])!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(firstPoint));
        context.Window.MouseDown(firstPoint, MouseButton.Left);
        context.Window.MouseUp(firstPoint, MouseButton.Left);
        var secondPoint = timeline.TranslatePoint(timeline.GetClipRectangle(originalIds[1])!.Value.Center, context.Window)!.Value;
        context.Window.MouseDown(secondPoint, MouseButton.Left, RawInputModifiers.Control);
        context.Window.MouseUp(secondPoint, MouseButton.Left, RawInputModifiers.Control);
        Assert.Equal(originalIds.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.All(context.Session.DocumentSnapshot.Layers, layer => Assert.Equal(LayerKind.SUBTITLE, layer.Kind));
        var trackId = context.Session.DocumentSnapshot.Tracks[0].Id;
        var header = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        var collapse = timeline.TranslatePoint(new(12, header.Top + 12), context.Window)!.Value;
        context.Window.MouseDown(collapse, MouseButton.Left);
        context.Window.MouseUp(collapse, MouseButton.Left);
        Assert.True(timeline.IsTrackCollapsed(trackId));
        Assert.All(originalIds, id => Assert.NotNull(timeline.GetClipRectangle(id)));
        Assert.Equal(originalIds.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
    }

    [AvaloniaFact]
    public void WheelMagnifyAndOverviewOnlyNavigateTheViewport()
    {
        var tracks = Enumerable.Range(0, 9).Select(index => new ProjectTrack { Name = $"Track {index}" }).ToArray();
        var document = new ProjectDocument { Tracks = [.. tracks] };
        using var timeline = new SubtitleTimelineControl();
        var overview = new TimelineOverviewControl();
        var grid = new Grid { RowDefinitions = new("28,*") };
        grid.Children.Add(overview);
        grid.Children.Add(timeline);
        Grid.SetRow(timeline, 1);
        timeline.SetDocument(document, null, null);
        timeline.SetViewport(new(20, 20), 120);
        var seeks = new List<MediaTime>();
        var edits = 0;
        timeline.SeekRequested += (_, e) => seeks.Add(e.Time);
        timeline.TimingChanged += (_, _) => edits++;
        timeline.ViewportChanged += (_, e) => overview.SetScene(document, e.Viewport, 120, new(7));
        overview.ViewportChanged += (_, e) => timeline.SetViewport(e.Viewport, 120);
        IPointer? pointer = null;
        timeline.PointerMoved += (_, e) => pointer = e.Pointer;
        var window = new Window { Width = 520, Height = 200, Content = grid };
        window.Show();
        try
        {
            Prepare(window);
            timeline.SetViewport(new(20, 20, Width: timeline.Viewport.Width, Height: timeline.Viewport.Height), 120);
            overview.SetScene(document, timeline.Viewport, 120, new(7));
            var local = new Point(timeline.HeaderWidth + 100, 80);
            var point = timeline.TranslatePoint(local, window)!.Value;
            window.MouseWheel(point, new(0, -2));
            Assert.Equal(72, timeline.Viewport.VerticalOffset);
            var start = timeline.ViewStart;
            window.MouseWheel(point, new(0, -2), RawInputModifiers.Shift);
            Assert.Equal(start + 96 / 20d, timeline.ViewStart, 8);
            var anchorTime = timeline.ViewStart + 100 / timeline.PixelsPerSecond;
            window.MouseWheel(point, new(0, 2), RawInputModifiers.Control);
            Assert.Equal(anchorTime, timeline.ViewStart + 100 / timeline.PixelsPerSecond, 8);
            var beforeMagnify = timeline.PixelsPerSecond;
            window.MouseMove(point);
            Assert.NotNull(pointer);
            timeline.RaiseEvent(new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent, timeline,
                pointer!, window, point, 0, new(), KeyModifiers.None, new(0.2, 0)));
            Assert.True(timeline.PixelsPerSecond > beforeMagnify);
            Assert.Equal(anchorTime, timeline.ViewStart + 100 / timeline.PixelsPerSecond, 8);
            Prepare(window);
            var view = overview.ViewportRectangle;
            var panStart = timeline.ViewStart;
            Drag(window, view.Center, view.Center + new Vector(40, 0));
            Assert.Equal(panStart + 40 / 520d * 120, timeline.ViewStart, 8);
            Prepare(window);
            view = overview.ViewportRectangle;
            var previousScale = timeline.PixelsPerSecond;
            Drag(window, new(view.Right, view.Center.Y), new(view.Right + 20, view.Center.Y));
            Assert.True(timeline.PixelsPerSecond < previousScale);
            Assert.Empty(seeks);
            Assert.Equal(0, edits);
            Assert.Equal(MediaTime.Zero, timeline.Position);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private static void Drag(Window window, Point start, Point end)
    {
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        window.MouseUp(end, MouseButton.Left);
    }


}
