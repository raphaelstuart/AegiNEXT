using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipMaskBadgeUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void BadgeRendersForStaticVectorAndAnimatedInverseMasksAndTracksClearUndoRedo(bool dark, int shape)
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { Start = new(1), End = new(4), Text = "中文 Mask ABC 123" };
        var plainCue = new SubtitleLine { Start = new(5), End = new(7), Text = "Plain ABC 中文 123" };
        var plainLayer = Layer(plainCue);
        var layer = Layer(cue) with
        {
            Mask = shape == 1 ? new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(10, 10) }] }] }
                : new RectangleClipMask { TopLeft = new(10, 10), BottomRight = new(400, 300), Inverted = shape == 2 },
            Tracks = shape == 2 ? [new(AnimationProperty.MASK_POSITION, [new(MediaTime.Zero, new ScenePoint()), new(new(1), new ScenePoint(50, 20))])] : []
        };
        var editor = new ProjectEditor(new() { Subtitles = [cue, plainCue], Layers = [layer, plainLayer] });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 90 };
        void Synchronize(object? sender, EventArgs args) => timeline.SetDocument(editor.Snapshot, plainCue.Id, plainLayer);
        editor.Changed += Synchronize;
        Synchronize(null, EventArgs.Empty);
        var window = new Window { Width = 860, Height = 320, Content = timeline, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Flush(window);
            Assert.Null(timeline.GetClipMaskBadgeRectangle(plainLayer.Id));
            var badge = timeline.GetClipMaskBadgeRectangle(layer.Id)!.Value;
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            Assert.True(clip.Contains(badge));
            Assert.Equal(clip.Center.Y, badge.Center.Y, 6);
            using var masked = Capture(timeline);
            Assert.True(ContrastPixels(masked, badge) > 10);
            if (shape == 0 && Environment.GetEnvironmentVariable("AEGINEXT_MASK_BADGE_CAPTURE_DIRECTORY") is { } captureDirectory)
            {
                Directory.CreateDirectory(captureDirectory);
                using var image = masked.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(captureDirectory, dark ? "timeline-mask-badge-dark.png" : "timeline-mask-badge-light.png"));
                image.SaveTo(file);
            }
            var original = editor.Snapshot;
            editor.ClearClipMask(layer.Id);
            Assert.Null(timeline.GetClipMaskBadgeRectangle(layer.Id));
            Assert.Null(ToolTip.GetTip(timeline));
            Assert.True(editor.Undo());
            Assert.Same(original, editor.Snapshot);
            Assert.Equal(badge, timeline.GetClipMaskBadgeRectangle(layer.Id));
            using var restored = Capture(timeline);
            Assert.Equal(Pixels(masked, badge), Pixels(restored, badge));
            Assert.True(editor.Redo());
            Assert.Null(timeline.GetClipMaskBadgeRectangle(layer.Id));
        }
        finally
        {
            editor.Changed -= Synchronize;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NarrowAndPartiallyScrolledClipsKeepTheirBadgeWithinTheVisibleClip(bool narrow, bool collapsed)
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { Start = new(1), End = narrow ? new(31, 30) : new(10), Text = "Long mixed 中文 ABC 123" };
        var layer = Layer(cue) with { Mask = new RectangleClipMask { BottomRight = new(400, 300) } };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60 };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        var window = new Window { Width = 560, Height = 180, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            if (collapsed)
            {
                timeline.ToggleTrackCollapse(cue.TrackId);
            }
            if (!narrow)
            {
                timeline.ViewStart = 2;
            }
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var badge = timeline.GetClipMaskBadgeRectangle(layer.Id)!.Value;
            Assert.True(clip.Contains(badge));
            Assert.True(new Rect(timeline.HeaderWidth, timeline.RulerHeight, timeline.Bounds.Width - timeline.HeaderWidth,
                timeline.Bounds.Height - timeline.RulerHeight).Contains(badge));
            using var masked = Capture(timeline);
            timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer with { Mask = null }] }, null, null);
            using var plain = Capture(timeline);
            Assert.False(Pixels(masked, badge).SequenceEqual(Pixels(plain, badge)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BadgeHoverLocalizesAndClickKeepsTheNormalClipSelectionAndTimingContract()
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { Start = new(1), End = new(4), Text = "Masked" };
        var layer = Layer(cue) with { Mask = new RectangleClipMask { BottomRight = new(400, 300) } };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 90 };
        timeline.SetDocument(document, null, null);
        var selected = Guid.Empty;
        var timingEdits = 0;
        timeline.ClipSelectionChanged += (_, e) => selected = e.Id;
        timeline.TimingChanged += (_, _) => timingEdits++;
        var window = new Window { Width = 650, Height = 220, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var badge = timeline.GetClipMaskBadgeRectangle(layer.Id)!.Value;
            window.MouseMove(badge.Center);
            Assert.Equal(Localization.Get("Workbench.ClipMask"), ToolTip.GetTip(timeline));
            Localization.SetLanguage("zh-CN");
            Assert.Equal("Clip 蒙版", ToolTip.GetTip(timeline));
            window.MouseDown(badge.Center, MouseButton.Left);
            window.MouseUp(badge.Center, MouseButton.Left);
            Assert.Equal(layer.Id, selected);
            Assert.Equal(0, timingEdits);
            Assert.Equal(cue.Start, layer.Start);
            Assert.Equal(cue.End, layer.End);
            window.MouseMove(new Point(timeline.HeaderWidth + 450, badge.Center.Y));
            Assert.Null(ToolTip.GetTip(timeline));
            window.MouseMove(badge.Center);
            Assert.NotNull(ToolTip.GetTip(timeline));
            window.Content = null;
            Assert.Null(ToolTip.GetTip(timeline));
            window.Content = timeline;
            Flush(window);
            window.MouseMove(badge.Center);
            Assert.Equal(Localization.Get("Workbench.ClipMask"), ToolTip.GetTip(timeline));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkbenchBadgeRefreshesFromCommittedMaskAndUndoWithoutReplacingTheTimeline()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        UiTestActions.CreateSubtitle(context);
        var layer = context.Session.SelectedLayer!;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.Null(timeline.GetClipMaskBadgeRectangle(layer.Id));
        context.Session.Editor.SetClipMask(layer.Id, new RectangleClipMask { BottomRight = new(400, 300) });
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(timeline.GetClipMaskBadgeRectangle(layer.Id));
        context.Session.MaskEditing.Clear();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(timeline.GetClipMaskBadgeRectangle(layer.Id));
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(timeline.GetClipMaskBadgeRectangle(layer.Id));
        Assert.Same(timeline, UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline"));
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static int ContrastPixels(SKBitmap pixels, Rect rectangle)
    {
        var colors = Pixels(pixels, rectangle);
        return colors.Count(color => color != colors[0]);
    }

    private static SKColor[] Pixels(SKBitmap pixels, Rect rectangle) =>
        Enumerable.Range((int)Math.Ceiling(rectangle.Top), Math.Max(1, (int)Math.Floor(rectangle.Height)))
            .SelectMany(y => Enumerable.Range((int)Math.Ceiling(rectangle.Left), Math.Max(1, (int)Math.Floor(rectangle.Width)))
                .Select(x => pixels.GetPixel(x, y))).ToArray();

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
