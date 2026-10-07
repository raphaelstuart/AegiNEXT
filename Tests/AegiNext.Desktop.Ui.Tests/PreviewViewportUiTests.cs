using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewViewportUiTests
{
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Meta)]
    [InlineData(RawInputModifiers.Control)]
    public void ZoomKeepsThePointUnderThePointerFixedAndLeavesFocusAlone(RawInputModifiers modifier)
    {
        using var environment = new UiTestEnvironment();
        var input = new TextBox { Text = "7e-" };
        var viewport = CreateViewport();
        var window = new Window { Width = 500, Height = 300, Content = new StackPanel { Children = { input, viewport } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            input.Focus();
            using var before = window.CaptureRenderedFrame();
            var anchor = new Point(123, 71);
            var point = viewport.TranslatePoint(anchor, window)!.Value;

            window.MouseWheel(point, new(0, 2), modifier);

            Assert.True(viewport.Zoom > 1);
            Assert.Equal(anchor.X, anchor.X * viewport.Zoom + viewport.Offset.X, 8);
            Assert.Equal(anchor.Y, anchor.Y * viewport.Zoom + viewport.Offset.Y, 8);
            Assert.Same(input, window.FocusManager!.GetFocusedElement());
            Assert.Equal("7e-", input.Text);
            using var after = window.CaptureRenderedFrame();
            Assert.NotEqual(0, before!.PixelSize.Width);
            var transform = Assert.IsType<MatrixTransform>(viewport.Child!.RenderTransform);
            Assert.Equal(viewport.Zoom, transform.Matrix.M11);
            Assert.Equal(viewport.Offset.X, transform.Matrix.M31);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PlainWheelScrollsTheParentAndZoomStaysWithinFiniteLimits()
    {
        using var environment = new UiTestEnvironment();
        var viewport = CreateViewport();
        var scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { viewport, new Border { Height = 800 } } }
        };
        var window = new Window { Width = 500, Height = 250, Content = scroller };
        try
        {
            window.Show();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            var point = viewport.TranslatePoint(new(80, 60), window)!.Value;
            window.MouseWheel(point, new(0, -2), RawInputModifiers.None);
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);
            Assert.True(scroller.Offset.Y > 0);
            scroller.Offset = default;
            window.UpdateLayout();
            window.MouseWheel(point, new(0, 10000), RawInputModifiers.Meta);
            Assert.Equal(16, viewport.Zoom);
            window.MouseWheel(point, new(0, -10000), RawInputModifiers.Meta);
            Assert.Equal(0.25, viewport.Zoom);
            Assert.True(double.IsFinite(viewport.Offset.X));
            Assert.True(double.IsFinite(viewport.Offset.Y));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LeftDragContinuesOutsideTheViewportAndReleaseEndsTheGesture()
    {
        using var environment = new UiTestEnvironment();
        var viewport = CreateViewport();
        var window = new Window { Width = 500, Height = 400, Content = new StackPanel { Children = { viewport } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            var start = viewport.TranslatePoint(new(40, 40), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(50, 220), RawInputModifiers.LeftMouseButton);
            Assert.Equal(new Vector(50, 220), viewport.Offset);
            window.MouseUp(start + new Vector(50, 220), MouseButton.Left);
            window.MouseMove(start + new Vector(100, 240));
            Assert.Equal(new Vector(50, 220), viewport.Offset);

            viewport.ResetView();
            Assert.Equal(1, viewport.Zoom);
            Assert.Equal(default, viewport.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LosingCaptureOrDetachingEndsPanWithoutChangingTheView()
    {
        using var environment = new UiTestEnvironment();
        var viewport = CreateViewport();
        var other = new Border { Height = 50, Background = Brushes.Blue };
        var window = new Window { Width = 500, Height = 300, Content = new StackPanel { Children = { viewport, other } } };
        IPointer? pointer = null;
        viewport.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            handledEventsToo: true);
        try
        {
            window.Show();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            var start = viewport.TranslatePoint(new(40, 40), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(20, 30), RawInputModifiers.LeftMouseButton);
            pointer!.Capture(other);
            window.MouseMove(start + new Vector(60, 70), RawInputModifiers.LeftMouseButton);
            Assert.Equal(new Vector(20, 30), viewport.Offset);
            pointer.Capture(null);
            window.MouseUp(start, MouseButton.Left);

            window.MouseDown(start, MouseButton.Left);
            Assert.IsType<StackPanel>(window.Content).Children.Remove(viewport);
            Assert.Null(pointer.Captured);
            window.MouseMove(start + new Vector(90, 90), RawInputModifiers.LeftMouseButton);
            Assert.Equal(new Vector(20, 30), viewport.Offset);
            window.MouseUp(start, MouseButton.Left);
        }
        finally
        {
            window.Close();
        }
    }

    private static PreviewViewportControl CreateViewport() => new()
    {
        Height = 180,
        Child = new Border { Background = Brushes.Red, IsHitTestVisible = false }
    };
}
