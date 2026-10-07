using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineRenderingWorkBudgetUiTests
{
    [AvaloniaFact]
    public void PlaybackPositionReusesVisibleStaticDrawingAndCurveSamples()
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(2048);
        using var timeline = new SubtitleTimelineControl { IsWaveformVisible = false, IsSpectrumVisible = false };
        timeline.SetDocument(document, null, null);
        var window = new Window { Width = 800, Height = 260, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            timeline.SetViewport(new(2000, 100), 5000);
            Capture(timeline);
            var builds = timeline.StaticDrawingBuildCount;
            var layouts = timeline.TextLayoutBuildCount;
            var samples = timeline.CurveSampleCount;
            Assert.InRange(timeline.VisibleClipProjectionCount, 1, 8);
            Assert.True(samples > 0);

            for (var frame = 0; frame < 8; frame++)
            {
                timeline.Position = new(2000 + frame, 1);
                Capture(timeline);
            }

            Assert.Equal(builds, timeline.StaticDrawingBuildCount);
            Assert.Equal(layouts, timeline.TextLayoutBuildCount);
            Assert.Equal(samples, timeline.CurveSampleCount);
            timeline.SetViewport(timeline.Viewport with { StartSeconds = 2002 }, 5000);
            Capture(timeline);
            Assert.True(timeline.StaticDrawingBuildCount > builds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LiveTimingEndUpdatesOnlyTheOverlayAndNeverRepartitionsTheFullProject()
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(2048);
        var layer = document.Layers[1000];
        using var timeline = new SubtitleTimelineControl { IsWaveformVisible = false, IsSpectrumVisible = false };
        timeline.SetDocument(document, layer.SubtitleId, layer, [layer.Id]);
        var window = new Window { Width = 800, Height = 260, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            timeline.SetViewport(new(2000, 100), 5000);
            timeline.SetTimingPreview(new(layer.SubtitleId!.Value, layer.Start, layer.End));
            Capture(timeline);
            var builds = timeline.StaticDrawingBuildCount;
            var ranges = timeline.RangeProjectionBuildCount;
            for (var frame = 0; frame < 8; frame++)
            {
                timeline.SetTimingPreview(new(layer.SubtitleId.Value, layer.Start, layer.End + new MediaTime(frame + 1, 20)));
                Capture(timeline);
            }

            Assert.Equal(builds, timeline.StaticDrawingBuildCount);
            Assert.Equal(ranges, timeline.RangeProjectionBuildCount);
            timeline.SetTimingPreview(null);
            Capture(timeline);
            Assert.Equal(layer.End - layer.Start, new MediaTime(1));
            Assert.Equal(layer.Start, document.Layers[1000].Start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverviewCachesAllClipsAcrossPositionViewportAndLiveEndUpdates()
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(2048);
        var overview = new TimelineOverviewControl();
        var viewport = new TimelineViewport(0, 100, 600, 200);
        var window = new Window { Width = 800, Height = 28, Content = overview, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            overview.SetScene(document, viewport, 5000, MediaTime.Zero,
                new(document.Subtitles[0].Id, MediaTime.Zero, new(1)));
            Capture(overview);
            var builds = overview.StaticDrawingBuildCount;
            for (var frame = 1; frame < 8; frame++)
            {
                overview.SetScene(document, viewport with { StartSeconds = frame }, 5000, new(frame),
                    new(document.Subtitles[0].Id, MediaTime.Zero, new(frame + 1)));
                Capture(overview);
            }
            Assert.Equal(builds, overview.StaticDrawingBuildCount);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Capture(overview);
            Assert.True(overview.StaticDrawingBuildCount > builds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectionThemePaletteAndAudioVisibilityInvalidateDrawingWithoutRebuildingTheClipIndex()
    {
        using var environment = new UiTestEnvironment();
        var document = CreateDocument(32);
        using var timeline = new SubtitleTimelineControl { IsWaveformVisible = false, IsSpectrumVisible = false };
        timeline.SetDocument(document, null, null);
        var window = new Window { Width = 800, Height = 260, Content = timeline, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            Capture(timeline);
            var indexes = timeline.VisibleClipIndexBuildCount;
            var builds = timeline.StaticDrawingBuildCount;
            var layer = document.Layers[1];
            timeline.SetDocument(document, layer.SubtitleId, layer, [layer.Id]);
            Capture(timeline);
            Assert.Equal(indexes, timeline.VisibleClipIndexBuildCount);
            Assert.True(timeline.StaticDrawingBuildCount > builds);
            builds = timeline.StaticDrawingBuildCount;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Capture(timeline);
            Assert.True(timeline.StaticDrawingBuildCount > builds);
            builds = timeline.StaticDrawingBuildCount;
            timeline.SetClipPalette(new() { SelectedClip = "#FF0000" });
            Capture(timeline);
            Assert.True(timeline.StaticDrawingBuildCount > builds);
            timeline.IsSpectrumVisible = true;
            timeline.IsWaveformVisible = true;
            Capture(timeline);
            Assert.InRange(timeline.CachedDrawingBytes, 1, TimelineDrawingCache.MAX_CONTROL_BYTES);
            timeline.Dispose();
            Assert.Equal(0, timeline.CachedDrawingBytes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReplacingLocalSpectrumRetainsTheUnchangedOverviewBitmap()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = new SubtitleTimelineControl();
        var overview = new SpectrogramData(8, 128, MediaTime.Zero, new(1), new byte[8 * 128]);
        var detail = new SpectrogramData(2, 128, new(1), new(1, 2), new byte[2 * 128]);
        timeline.SetSpectrogram(null, overview);
        var overviewBuilds = timeline.SpectrumOverviewBitmapBuildCount;
        timeline.SetSpectrogram(detail, overview);
        Assert.Equal(overviewBuilds, timeline.SpectrumOverviewBitmapBuildCount);
        var detailBuilds = timeline.SpectrumBitmapBuildCount;
        timeline.SetSpectrogram(detail, overview);
        Assert.Equal(detailBuilds, timeline.SpectrumBitmapBuildCount);
        timeline.SetSpectrogram(null, overview);
        Assert.Equal(overviewBuilds, timeline.SpectrumOverviewBitmapBuildCount);
    }

    private static ProjectDocument CreateDocument(int count)
    {
        var cues = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Start = new(index * 2), End = new(index * 2 + 1), Text = $"字幕 ABC {index}"
        }).ToArray();
        return new()
        {
            Subtitles = [.. cues], Layers = [.. cues.Select(cue => new ProjectLayer
            {
                SubtitleId = cue.Id, Kind = LayerKind.SUBTITLE, Start = cue.Start, End = cue.End,
                Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.2), new(new(1), 0.8)])]
            })]
        };
    }

    private static void Capture(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        bitmap.Render(control);
    }
}
