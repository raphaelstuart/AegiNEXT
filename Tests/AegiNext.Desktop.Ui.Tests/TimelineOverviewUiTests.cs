using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineOverviewUiTests
{
    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(12)]
    public void VisibleHandlesZoomContinuouslyWhenTheViewportExtendsPastTheProject(double start)
    {
        var document = new ProjectDocument();
        using var timeline = new SubtitleTimelineControl();
        var overview = new TimelineOverviewControl();
        var window = CreateWindow(overview, timeline);
        var seeks = 0;
        var edits = 0;
        timeline.SeekRequested += (_, _) => seeks++;
        timeline.TimingChanged += (_, _) => edits++;
        EventHandler<TimelineViewportEventArgs> navigate = (_, e) =>
        {
            timeline.SetViewport(e.Viewport, 10);
            overview.SetScene(document, timeline.Viewport, 10, new(4));
        };
        overview.ViewportChanged += navigate;
        timeline.SetDocument(document, null, null);
        timeline.Position = new(4);
        window.Show();
        try
        {
            Prepare(window);
            timeline.SetViewport(new(start, timeline.Viewport.Width / 8.111), 10);
            overview.SetScene(document, timeline.Viewport, 10, new(4));
            Prepare(window);
            var rectangle = overview.ViewportRectangle;
            Assert.True(rectangle.Left >= 0);
            Assert.True(rectangle.Right < overview.Bounds.Width);
            Assert.True(rectangle.Width > 0);
            var right = overview.TranslatePoint(new(rectangle.Right, rectangle.Center.Y), window)!.Value;
            Assert.Same(overview, window.InputHitTest(right));
            var originalScale = timeline.PixelsPerSecond;
            var originalDuration = timeline.VisibleDuration;
            window.MouseDown(right, MouseButton.Left);
            window.MouseMove(right);
            Assert.Equal(start, timeline.ViewStart, 8);
            Assert.Equal(originalScale, timeline.PixelsPerSecond, 8);

            var rightTarget = right - new Vector(20, 0);
            window.MouseMove(rightTarget);
            window.MouseUp(rightTarget, MouseButton.Left);
            Assert.Equal(start, timeline.ViewStart, 8);
            Assert.Equal(originalDuration - 0.5, timeline.VisibleDuration, 8);
            Assert.True(timeline.PixelsPerSecond > originalScale);
            Assert.True(timeline.ViewStart + timeline.VisibleDuration > 10);
            Assert.True(double.IsFinite(timeline.PixelsPerSecond));
            Prepare(window);

            rectangle = overview.ViewportRectangle;
            Assert.True(rectangle.Right < overview.Bounds.Width);
            var left = overview.TranslatePoint(new(rectangle.Left, rectangle.Center.Y), window)!.Value;
            Assert.Same(overview, window.InputHitTest(left));
            var end = timeline.ViewStart + timeline.VisibleDuration;
            originalScale = timeline.PixelsPerSecond;
            window.MouseDown(left, MouseButton.Left);
            window.MouseMove(left);
            Assert.Equal(start, timeline.ViewStart, 8);
            Assert.Equal(originalScale, timeline.PixelsPerSecond, 8);
            var leftTarget = left - new Vector(20, 0);
            window.MouseMove(leftTarget);
            window.MouseUp(leftTarget, MouseButton.Left);
            Assert.Equal(start - 0.5, timeline.ViewStart, 8);
            Assert.Equal(end, timeline.ViewStart + timeline.VisibleDuration, 8);
            Assert.True(double.IsFinite(timeline.ViewStart));
            Assert.True(double.IsFinite(timeline.PixelsPerSecond));
            Assert.True(timeline.PixelsPerSecond > 0);
            Assert.Equal(0, seeks);
            Assert.Equal(0, edits);
            Assert.Equal(new MediaTime(4), timeline.Position);

            Prepare(window);
            rectangle = overview.ViewportRectangle;
            Assert.True(rectangle.Left >= 0);
            Assert.True(rectangle.Right < overview.Bounds.Width);
            var pan = overview.TranslatePoint(new(40, rectangle.Center.Y), window)!.Value;
            Assert.Same(overview, window.InputHitTest(pan));
            window.MouseDown(pan, MouseButton.Left);
            window.MouseMove(pan + new Vector(20, 0));
            window.MouseUp(pan + new Vector(20, 0), MouseButton.Left);
            Assert.InRange(timeline.ViewStart, 0, Math.Max(0, 10 - timeline.VisibleDuration));
            Assert.Equal(0, seeks);
            Assert.Equal(0, edits);
            Assert.Equal(new MediaTime(4), timeline.Position);
        }
        finally
        {
            overview.ViewportChanged -= navigate;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TwentyFourPixelOverviewRendersAllSixteenTracksAndStillNavigatesWithoutSeeking()
    {
        var tracks = Enumerable.Range(0, 16).Select(index => new SubtitleTrack { Name = $"Track {index}" }).ToArray();
        var empty = new ProjectDocument { SubtitleTracks = [.. tracks] };
        var cues = tracks.Select((track, index) => new SubtitleLine
        {
            TrackId = track.Id,
            Start = new(index + 2, 2),
            End = new((index + 2) * 5 + 2, 10)
        }).ToArray();
        var document = empty with
        {
            Subtitles = [.. cues],
            Layers = [.. cues.Select(cue => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
            })]
        };
        using var timeline = new SubtitleTimelineControl();
        var overview = new TimelineOverviewControl();
        var window = CreateWindow(overview, timeline);
        var seeks = 0;
        var edits = 0;
        timeline.SeekRequested += (_, _) => seeks++;
        timeline.TimingChanged += (_, _) => edits++;
        EventHandler<TimelineViewportEventArgs> navigate = (_, e) =>
        {
            timeline.SetViewport(e.Viewport, 10);
            overview.SetScene(document, timeline.Viewport, 10, new(4));
        };
        overview.ViewportChanged += navigate;
        timeline.SetDocument(empty, null, null);
        timeline.Position = new(4);
        window.Show();
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 150), 10);
            overview.SetScene(empty, timeline.Viewport, 10, new(4));
            using var baseline = Capture(window);
            Assert.Equal(24, overview.Bounds.Height);
            timeline.SetDocument(document, null, null);
            overview.SetScene(document, timeline.Viewport, 10, new(4));
            using var rendered = Capture(window);
            var origin = overview.TranslatePoint(new(), window)!.Value;
            var top = (int)Math.Round(origin.Y * window.RenderScaling);
            var bottom = (int)Math.Round((origin.Y + overview.Bounds.Height) * window.RenderScaling);
            foreach (var cue in cues)
            {
                var middle = ((double)cue.Start.Numerator / cue.Start.Denominator +
                    (double)cue.End.Numerator / cue.End.Denominator) / 2;
                var x = (int)Math.Round((origin.X + middle / 10 * overview.Bounds.Width) * window.RenderScaling);
                var changed = Enumerable.Range(top, bottom - top)
                    .Where(y => baseline.GetPixel(x, y) != rendered.GetPixel(x, y)).ToArray();
                Assert.NotEmpty(changed);
                if (cue.Id == cues[^1].Id)
                {
                    Assert.Contains(changed, y => y >= top + (bottom - top) / 2);
                }
            }

            var rectangle = overview.ViewportRectangle;
            var right = overview.TranslatePoint(new(rectangle.Right, rectangle.Center.Y), window)!.Value;
            Assert.Same(overview, window.InputHitTest(right));
            var scale = timeline.PixelsPerSecond;
            window.MouseDown(right, MouseButton.Left);
            window.MouseMove(right + new Vector(20, 0));
            window.MouseUp(right + new Vector(20, 0), MouseButton.Left);
            Assert.True(timeline.PixelsPerSecond < scale);
            Prepare(window);
            var center = overview.TranslatePoint(overview.ViewportRectangle.Center, window)!.Value;
            Assert.Same(overview, window.InputHitTest(center));
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(center + new Vector(20, 0));
            window.MouseUp(center + new Vector(20, 0), MouseButton.Left);
            Assert.Equal(0.5, timeline.ViewStart, 8);
            Assert.Equal(0, seeks);
            Assert.Equal(0, edits);
            Assert.Equal(new MediaTime(4), timeline.Position);
        }
        finally
        {
            overview.ViewportChanged -= navigate;
            window.Close();
        }
    }

    private static Window CreateWindow(TimelineOverviewControl overview, SubtitleTimelineControl timeline)
    {
        var grid = new Grid { RowDefinitions = new("24,*") };
        grid.Children.Add(overview);
        grid.Children.Add(timeline);
        Grid.SetRow(timeline, 1);
        return new() { Width = 400, Height = 180, Content = grid };
    }

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private static SKBitmap Capture(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
