using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineScrubbingUiTests
{
    [AvaloniaFact]
    public void PausedTimelineRepaintsWhenThemeChangesWithoutMovingThePlayhead()
    {
        using var timeline = new SubtitleTimelineControl();
        var window = new Window
        {
            Width = 500, Height = 200, Content = timeline, RequestedThemeVariant = ThemeVariant.Light
        };
        window.Show();
        try
        {
            Assert.Equal(new SKColor(242, 245, 250), ReadBackground(window));
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.Equal(new SKColor(19, 26, 38), ReadBackground(window));
            Assert.Equal(MediaTime.Zero, timeline.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DragRespondsBeforeDecodeDeduplicatesFramesAndFlushesReleasePosition()
    {
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 120 };
        var window = new Window { Width = 500, Height = 200, Content = timeline };
        var targets = new List<MediaTime>();
        timeline.SeekRequested += (_, e) =>
        {
            targets.Add(e.Time);
            timeline.Position = MediaTime.Zero;
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var rulerOrigin = timeline.HeaderWidth;
            window.MouseDown(new Point(rulerOrigin + 40, 12), MouseButton.Left);
            Assert.Equal(new MediaTime(1, 3), timeline.Position);
            Assert.Single(targets);
            window.MouseMove(new Point(rulerOrigin + 40.1, 12));
            Assert.Single(targets);
            window.MouseMove(new Point(rulerOrigin + 44, 12));
            Assert.Equal(new MediaTime(11, 30), timeline.Position);
            Assert.Equal(2, targets.Count);
            window.MouseUp(new Point(rulerOrigin + 80, 12), MouseButton.Left);
            Assert.Equal(new MediaTime(2, 3), timeline.Position);
            Assert.Equal(new MediaTime(2, 3), targets[^1]);
            Assert.Equal(3, targets.Count);
            Assert.False(timeline.HasActiveDrag);
            timeline.Position = new(1);
            Assert.Equal(new MediaTime(1), timeline.Position);
        }
        finally
        {
            window.Close();
        }
    }

    private static SKColor ReadBackground(Window window)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        return pixels.GetPixel(200, 20);
    }
}
