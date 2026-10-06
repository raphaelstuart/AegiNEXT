using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskOverlayUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectedOutlineAndExcludedShadeRemainAfterEndingEditingAndNeverReachTransport(bool vector, bool inverted)
    {
        var line = new SubtitleLine { End = new(5) };
        ClipMask mask = vector
            ? new VectorClipMask { Contours = [Contour(new(100, 50), new(600, 50), new(600, 700), new(100, 700))], Inverted = inverted }
            : new RectangleClipMask { TopLeft = new(100, 50), BottomRight = new(600, 700), Inverted = inverted };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = vector ? CanvasEditMode.MASK_VECTOR : CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(0));
        var host = new Grid { RowDefinitions = new("300,100") };
        host.Children.Add(canvas);
        var transport = new Border { Background = Brushes.DarkSlateGray };
        Grid.SetRow(transport, 1);
        host.Children.Add(transport);
        var window = new Window { Width = 600, Height = 400, Content = host };
        window.Show();
        try
        {
            Flush(window);
            var board = canvas.ProjectRectangle;
            using var editing = Capture(host);
            Assert.True(CountGold(editing, board) > 20);
            AssertNoGoldOutside(editing, board);
            Assert.True(canvas.TryExecuteFocusCommand(WorkbenchCommand.END_TEXT_INPUT, canvas));
            Assert.Equal(CanvasEditMode.POSITION, canvas.EditMode);
            Assert.Null(canvas.Cursor);
            Assert.False(canvas.HasActiveDrag);
            Assert.Same(mask, layer.Mask);
            Flush(window);
            using var passive = Capture(host);
            Assert.True(CountGold(passive, board) > 20);
            AssertNoGoldOutside(passive, board);
            var inside = Sample(passive, board, 300, 150);
            var outside = Sample(passive, board, 900, 400);
            AssertGray(inverted ? inside : outside);
            AssertDark(inverted ? outside : inside);
            Assert.Equal(editing.GetPixel(300, 350), passive.GetPixel(300, 350));
            canvas.SetScene(document, null, new(0));
            Flush(window);
            using var deselected = Capture(host);
            Assert.Equal(0, CountGold(deselected, board));
            AssertDark(Sample(deselected, board, 900, 400));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludedShadeRespectsNonzeroContourHolesAndInverse(bool inverted)
    {
        var line = new SubtitleLine { End = new(5) };
        var mask = new VectorClipMask
        {
            Inverted = inverted,
            Contours =
            [
                Contour(new(100, 50), new(600, 50), new(600, 450), new(100, 450)),
                Contour(new(250, 150), new(250, 300), new(400, 300), new(400, 150))
            ]
        };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, layer, new(0));
        var window = new Window { Width = 600, Height = 300, Content = canvas };
        window.Show();
        try
        {
            Flush(window);
            using var pixels = Capture(canvas);
            var fill = Sample(pixels, canvas.ProjectRectangle, 150, 150);
            var hole = Sample(pixels, canvas.ProjectRectangle, 300, 220);
            var outside = Sample(pixels, canvas.ProjectRectangle, 900, 400);
            AssertGray(inverted ? fill : hole);
            AssertDark(inverted ? hole : fill);
            if (inverted)
            {
                AssertDark(outside);
            }
            else
            {
                AssertGray(outside);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static MaskContour Contour(params ScenePoint[] points) => new()
    {
        Nodes = points.Select(point => new MaskNode { Position = point }).ToImmutableArray()
    };

    private static SKColor Sample(SKBitmap pixels, Rect board, double x, double y) =>
        pixels.GetPixel((int)(board.X + x * board.Width / 1000), (int)(board.Y + y * board.Height / 500));

    private static void AssertGray(SKColor color)
    {
        Assert.InRange(color.Red, 15, 80);
        Assert.Equal(color.Red, color.Green);
        Assert.Equal(color.Red, color.Blue);
    }

    private static void AssertDark(SKColor color) => Assert.InRange(color.Red + color.Green + color.Blue, 0, 3);

    private static bool IsGold(SKColor color) => color.Red > 180 && color.Green > 120 && color.Blue < 80;

    private static int CountGold(SKBitmap pixels, Rect board)
    {
        var count = 0;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (board.Contains(new Point(x + 0.5, y + 0.5)) && IsGold(pixels.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static void AssertNoGoldOutside(SKBitmap pixels, Rect board)
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (!board.Inflate(1).Contains(new Point(x + 0.5, y + 0.5)))
                {
                    Assert.False(IsGold(pixels.GetPixel(x, y)), $"Mask overlay escaped video at ({x}, {y}).");
                }
            }
        }
    }

    private static SKBitmap Capture(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
