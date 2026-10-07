using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MotionPathOverlayUiTests
{
    [AvaloniaTheory]
    [InlineData(700, 400, -80, 0)]
    [InlineData(700, 400, 480, 0)]
    [InlineData(400, 700, 200, -280)]
    [InlineData(400, 700, 200, 280)]
    public async Task ControlHandleInPreviewLetterboxIsVisibleAndDraggable(int width, int height, double controlX, double controlY)
    {
        var path = new PathGeometry(new(0, 0), [new(new(controlX, controlY), new(90, 0), new(180, 0))]);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(2),
            Transform = new(0, 200), MotionPath = new(path, new(2))
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Layers = [layer] };
        ProjectValidator.Validate(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = new Window { Width = width, Height = height, Content = canvas };
        window.Show();
        try
        {
            using var pixels = await CaptureAsync(window, canvas);
            if (Environment.GetEnvironmentVariable("AEGINEXT_TEST_VISUAL_OUTPUT") is { Length: > 0 } outputDirectory)
            {
                Directory.CreateDirectory(outputDirectory);
                using var image = SKImage.FromBitmap(pixels);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(Path.Combine(outputDirectory, $"path-handle-{width}-{height}-{controlX}-{controlY}.png"));
                data.SaveTo(output);
            }
            var board = canvas.ProjectRectangle;
            var point = new Point(board.X + controlX, board.Y + 200 + controlY);
            Assert.False(board.Contains(point));
            Assert.True(new Rect(canvas.Bounds.Size).Contains(point));
            Assert.Same(canvas, window.InputHitTest(point));
            var marker = pixels.GetPixel((int)point.X, (int)point.Y);
            Assert.True(marker.Red > 200 && marker.Green > 200 && marker.Blue > 200,
                $"White path handle must remain visible in preview letterbox, got {marker} at {point}.");
            Assert.Equal(new SKColor(17, 21, 28), pixels.GetPixel(20, 20));

            window.MouseDown(point, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(point + new Vector(12, 8));
            Assert.Empty(edits);
            window.MouseUp(point + new Vector(12, 8), MouseButton.Left);
            var edit = Assert.Single(edits);
            Assert.Equal(new ScenePoint(controlX + 12, controlY + 8), edit.Path!.Path.Segments[0].Control1);
            Assert.Equal(layer.Transform, edit.Transform);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShiftClickInLetterboxSplitsTheVisibleCurveOnRelease()
    {
        var path = new PathGeometry(new(0, 0), [new(new(30, 260), new(90, 260), new(120, 0))]);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(2),
            Transform = new(100, 300), MotionPath = new(path, new(2))
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasLayerEditEventArgs>();
        canvas.LayerEdited += (_, e) => edits.Add(e);
        var window = new Window { Width = 400, Height = 700, Content = canvas };
        window.Show();
        try
        {
            using var pixels = await CaptureAsync(window, canvas);
            var point = new Point(canvas.ProjectRectangle.X + 160, canvas.ProjectRectangle.Y + 495);
            Assert.False(canvas.ProjectRectangle.Contains(point));
            Assert.Same(canvas, window.InputHitTest(point));
            var curve = pixels.GetPixel((int)point.X, (int)point.Y);
            Assert.True(curve.Red < 40 && curve.Green > 80 && curve.Blue > 140, $"Curve must be visible in preview letterbox, got {curve}.");
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.True(canvas.HasActiveDrag);
            Assert.Empty(edits);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            var result = Assert.Single(edits).Path!.Path;
            Assert.Equal(2, result.Segments.Length);
            Assert.Equal(60, result.Segments[0].End.X, 5);
            Assert.Equal(195, result.Segments[0].End.Y, 5);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<SKBitmap> CaptureAsync(Window window, EffectCanvasControl canvas)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            Assert.True(DateTime.UtcNow < deadline, "Canvas did not present its latest preview sequence.");
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using (var submitted = window.CaptureRenderedFrame())
            {
                Assert.NotNull(submitted);
            }
            var sequence = canvas.PreviewSequence;
            await canvas.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, TestContext.Current.CancellationToken);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (canvas.PreviewSequence != sequence || canvas.PresentedPreviewSequence != sequence)
            {
                await Task.Yield();
                continue;
            }
            using var stream = new MemoryStream();
            frame.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
    }
}
