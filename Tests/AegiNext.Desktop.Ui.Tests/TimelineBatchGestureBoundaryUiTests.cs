using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineBatchGestureBoundaryUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false, 277)]
    [InlineData(false, true, 233)]
    [InlineData(true, false, 247)]
    [InlineData(true, true, 233)]
    public void AltBypassesBothTheVisibleStepAndClipSnapForEverySelectedMember(bool snap, bool alt, int offsetMilliseconds)
    {
        var first = Shape(new(123, 1000), new(1123, 1000));
        var second = Shape(new(3456, 1000), new(4456, 1000));
        var boundary = Shape(new(137, 100), new(237, 100));
        var document = new ProjectDocument { Layers = [first, second, boundary] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = snap, IsStepEnabled = true };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var origin = timeline.GetClipRectangle(first.Id)!.Value.Center;
            var destination = origin + new Vector(23.3, 0);
            var modifiers = alt ? RawInputModifiers.Alt : RawInputModifiers.None;
            window.MouseDown(origin, MouseButton.Left, modifiers);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(destination, modifiers);
            Assert.Same(document, editor.Snapshot);
            Assert.Equal(snap && !alt ? boundary.Start : (MediaTime?)null, timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left, modifiers);

            var offset = new MediaTime(offsetMilliseconds, 1000);
            Assert.Equal(first.Start + offset, editor.Snapshot.Layers.Single(layer => layer.Id == first.Id).Start);
            Assert.Equal(second.Start + offset, editor.Snapshot.Layers.Single(layer => layer.Id == second.Id).Start);
            Assert.Equal(boundary, editor.Snapshot.Layers.Single(layer => layer.Id == boundary.Id));
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrabbedClipStartOrEndSnapProducesOneExactOffsetForTheWholeSelection(bool endEdge)
    {
        var first = Shape(new(1), new(2));
        var second = Shape(new(4), new(5));
        var boundary = Shape(endEdge ? new(301, 100) : new(201, 100), endEdge ? new(351, 100) : new(251, 100));
        var document = new ProjectDocument { Layers = [first, second, boundary] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = true };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var origin = timeline.GetClipRectangle(first.Id)!.Value.Center;
            var destination = origin + new Vector(100, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Equal(boundary.Start, timeline.SnapTarget);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(destination, MouseButton.Left);

            Assert.Equal(new MediaTime(201, 100), editor.Snapshot.Layers.Single(layer => layer.Id == first.Id).Start);
            Assert.Equal(new MediaTime(501, 100), editor.Snapshot.Layers.Single(layer => layer.Id == second.Id).Start);
            Assert.All(editor.Snapshot.Layers.Where(layer => layer.Id != boundary.Id), layer => Assert.Equal(new MediaTime(1), layer.End - layer.Start));
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UngrabbedMembersNearAnExternalBoundaryDoNotSnapTheGroup()
    {
        var first = Shape(new(1), new(2));
        var second = Shape(new(401, 100), new(501, 100));
        var boundary = Shape(new(502, 100), new(602, 100));
        var document = new ProjectDocument { Layers = [first, second, boundary] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = true };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var origin = timeline.GetClipRectangle(first.Id)!.Value.Center;
            var destination = origin + new Vector(100, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Null(timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left);

            Assert.Equal(new MediaTime(2), editor.Snapshot.Layers.Single(layer => layer.Id == first.Id).Start);
            Assert.Equal(new MediaTime(501, 100), editor.Snapshot.Layers.Single(layer => layer.Id == second.Id).Start);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EverySelectedMembersOriginalBoundaryIsExcludedFromSnapCandidates()
    {
        var first = Shape(new(1), new(2));
        var second = Shape(new(301, 100), new(401, 100));
        var document = new ProjectDocument { Layers = [first, second] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = true };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var origin = timeline.GetClipRectangle(first.Id)!.Value.Center;
            var destination = origin + new Vector(103.3, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Null(timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left);

            var offset = new MediaTime(31, 30);
            Assert.Equal(first.Start + offset, editor.Snapshot.Layers.Single(layer => layer.Id == first.Id).Start);
            Assert.Equal(second.Start + offset, editor.Snapshot.Layers.Single(layer => layer.Id == second.Id).Start);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("capture")]
    [InlineData("selection")]
    [InlineData("detach")]
    public void LifecycleCancellationClearsEveryPreviewAndReleaseCannotCreateAnUndo(string cancellation)
    {
        var first = Shape(new(1), new(2));
        var second = Shape(new(4), new(5));
        var document = new ProjectDocument { Layers = [first, second] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = false };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        IPointer? pointer = null;
        timeline.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var firstRectangle = timeline.GetClipRectangle(first.Id)!.Value;
            var secondRectangle = timeline.GetClipRectangle(second.Id)!.Value;
            var origin = firstRectangle.Center;
            var destination = origin + new Vector(100, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.True(timeline.HasActiveDrag);
            Assert.NotEqual(firstRectangle, timeline.GetClipRectangle(first.Id));
            Assert.NotEqual(secondRectangle, timeline.GetClipRectangle(second.Id));
            Assert.Same(document, editor.Snapshot);
            if (cancellation == "capture")
            {
                Assert.NotNull(pointer);
                pointer.Capture(null);
            }
            else if (cancellation == "selection")
            {
                timeline.SetDocument(document, null, first, [first.Id]);
            }
            else
            {
                window.Content = null;
                window.Content = timeline;
                window.UpdateLayout();
            }

            Assert.False(timeline.HasActiveDrag);
            Assert.Null(timeline.SnapTarget);
            Assert.Null(timeline.Cursor);
            Assert.Equal(firstRectangle, timeline.GetClipRectangle(first.Id));
            Assert.Equal(secondRectangle, timeline.GetClipRectangle(second.Id));
            window.MouseUp(destination, MouseButton.Left);
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(timeline.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroHorizontalDeltaPreservesNonFrameAlignedTimesEvenWithStepAndNearbySnap(bool returnToOrigin)
    {
        var first = Shape(new(123, 1000), new(1123, 1000));
        var second = Shape(new(3456, 1000), new(4456, 1000));
        var boundary = Shape(new(125, 1000), new(875, 1000));
        var document = new ProjectDocument { Layers = [first, second, boundary] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = true, IsStepEnabled = true };
        Connect(timeline, editor, first, [first.Id, second.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var firstRectangle = timeline.GetClipRectangle(first.Id)!.Value;
            var secondRectangle = timeline.GetClipRectangle(second.Id)!.Value;
            var origin = firstRectangle.Center;
            window.MouseDown(origin, MouseButton.Left);
            if (returnToOrigin)
            {
                window.MouseMove(origin + new Vector(30, 0));
                Assert.NotEqual(firstRectangle, timeline.GetClipRectangle(first.Id));
            }

            var destination = origin + new Vector(0, 40);
            window.MouseMove(destination);
            Assert.Null(timeline.SnapTarget);
            Assert.Equal(firstRectangle, timeline.GetClipRectangle(first.Id));
            Assert.Equal(secondRectangle, timeline.GetClipRectangle(second.Id));
            window.MouseUp(destination, MouseButton.Left);

            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(timeline.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSubtitleAndShapeShareAnOffsetAndSubtitleCollisionInvalidatesTheShapeToo(bool collision)
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2), Text = "Moving subtitle" };
        var first = new ProjectLayer
        {
            Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
        };
        var shape = Shape(new(4), new(5));
        var obstacle = new SubtitleLine
        {
            Start = collision ? new(3) : new(10), End = collision ? new(4) : new(11), Text = "Obstacle"
        };
        var obstacleLayer = new ProjectLayer
        {
            Id = obstacle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = obstacle.Id, Start = obstacle.Start, End = obstacle.End
        };
        var document = new ProjectDocument { Subtitles = [cue, obstacle], Layers = [first, shape, obstacleLayer] };
        var editor = new ProjectEditor(document);
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = false };
        Connect(timeline, editor, first, [first.Id, shape.Id]);
        var window = new Window { Width = 900, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window, timeline);
            var firstRectangle = timeline.GetClipRectangle(first.Id)!.Value;
            var shapeRectangle = timeline.GetClipRectangle(shape.Id)!.Value;
            var origin = firstRectangle.Center;
            var destination = origin + new Vector(200, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Equal(firstRectangle.Left + 200, timeline.GetClipRectangle(first.Id)!.Value.Left, 6);
            Assert.Equal(shapeRectangle.Left + 200, timeline.GetClipRectangle(shape.Id)!.Value.Left, 6);
            Assert.Same(document, editor.Snapshot);
            if (collision)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using var stream = new MemoryStream();
                frame.Save(stream, PngBitmapEncoderOptions.Default);
                stream.Position = 0;
                using var image = SKBitmap.Decode(stream);
                foreach (var id in new[] { first.Id, shape.Id })
                {
                    var rectangle = timeline.GetClipRectangle(id)!.Value;
                    var pixel = image.GetPixel((int)(rectangle.Right - 10), (int)(rectangle.Bottom - 5));
                    Assert.True(pixel.Red > pixel.Green + 15 && pixel.Red > pixel.Blue + 10,
                        $"Mixed moving member {id} must show the invalid red fill; actual pixel is {pixel}.");
                }
            }

            window.MouseUp(destination, MouseButton.Left);
            if (collision)
            {
                Assert.Same(document, editor.Snapshot);
                Assert.Equal(firstRectangle, timeline.GetClipRectangle(first.Id));
                Assert.Equal(shapeRectangle, timeline.GetClipRectangle(shape.Id));
                Assert.False(editor.CanUndo);
            }
            else
            {
                Assert.Equal(new MediaTime(3), editor.Snapshot.Subtitles.Single(line => line.Id == cue.Id).Start);
                Assert.Equal(new MediaTime(6), editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id).Start);
                Assert.Equal(shape.Shape, editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id).Shape);
                Assert.True(editor.Undo());
                Assert.Same(document, editor.Snapshot);
                Assert.False(editor.CanUndo);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer Shape(MediaTime start, MediaTime end) => new()
    {
        Kind = LayerKind.SHAPE, Start = start, End = end, Shape = new(ShapeKind.RECTANGLE, 40, 20)
    };

    private static void Connect(SubtitleTimelineControl timeline, ProjectEditor editor, ProjectLayer primary, Guid[] selectedIds)
    {
        timeline.SetDocument(editor.Snapshot, null, primary, selectedIds);
        timeline.ClipSelectionChanged += (_, selection) =>
        {
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers.Single(layer => layer.Id == selection.Id), selection.SelectedIds);
            selection.SelectionAccepted = true;
        };
        timeline.ClipsMoveCompleted += (_, move) =>
        {
            editor.ShiftClips(move.LayerIds, move.Offset);
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers.Single(layer => layer.Id == move.PrimaryId), move.LayerIds);
        };
        timeline.TimingChanged += (_, _) => Assert.Fail("A multi-clip move must use its batch semantic event.");
    }

    private static void Prepare(Window window, SubtitleTimelineControl timeline)
    {
        window.UpdateLayout();
        timeline.PixelsPerSecond = 100;
        timeline.ViewStart = 0;
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
