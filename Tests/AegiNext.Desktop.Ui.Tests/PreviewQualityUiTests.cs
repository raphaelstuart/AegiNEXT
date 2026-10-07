using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Preview;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Core.Timing;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewQualityUiTests
{
    [AvaloniaTheory]
    [InlineData(PreviewQuality.LOWEST, "低清", "Low", 568, 320)]
    [InlineData(PreviewQuality.HIGH, "高清", "High", 1920, 1080)]
    public async Task QualityCanBeChosenBeforeMediaAndPersistsAcrossLanguageRefreshWithoutChangingTheProject(
        PreviewQuality quality, string chineseLabel, string englishLabel, int width, int height)
    {
        await using var context = new MainWindowTestContext();
        var selector = UiTestActions.Find<ComboBox>(context.Window, "QualityCombo");
        Assert.True(selector.IsEffectivelyEnabled);
        Assert.Equal(PreviewQuality.LOW, Assert.IsType<PreviewQualityChoice>(selector.SelectedItem).Id);
        Assert.Equal(new[] { PreviewQuality.LOWEST, PreviewQuality.LOW, PreviewQuality.STANDARD, PreviewQuality.HIGH },
            selector.Items.OfType<PreviewQualityChoice>().Select(choice => choice.Id));
        var document = context.Session.DocumentSnapshot;
        Choose(selector, quality);
        Assert.Equal(quality, context.Session.Preferences.PreviewQuality);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.HasUnsavedChanges);

        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "zh-CN" });
        Assert.Equal(quality, Assert.IsType<PreviewQualityChoice>(selector.SelectedItem).Id);
        Assert.Contains(chineseLabel, Assert.IsType<PreviewQualityChoice>(selector.SelectedItem).Label, StringComparison.Ordinal);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = "en-US" });
        Assert.Equal(quality, Assert.IsType<PreviewQualityChoice>(selector.SelectedItem).Id);
        Assert.Contains(englishLabel, Assert.IsType<PreviewQualityChoice>(selector.SelectedItem).Label, StringComparison.Ordinal);
        await DrainAsync(() => context.Session.PreferencesStore.Load().PreviewQuality == quality &&
                               context.Session.PreferencesStore.Load().Language == "en-US");
        Assert.Equal(new PixelSize(width, height), UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").MaximumPreviewSize);
    }

    [AvaloniaFact]
    public async Task SwitchingWhilePausedRefreshesTheSameFrameAndKeepsTrackAndClipSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration is not null);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(3), "Quality");
        context.Session.SelectCue(cueId);
        await context.Controller.SeekAsync(new(1));
        await DrainAsync(() => context.Controller.Snapshot.PresentedAtPosition == new AegiNext.Core.Timing.MediaTime(1));
        var document = context.Session.DocumentSnapshot;
        var generation = context.Controller.Snapshot.PresentedGeneration!.Value;
        var trackId = context.Session.CurrentTrackId;
        Choose(UiTestActions.Find<ComboBox>(context.Window, "QualityCombo"), PreviewQuality.HIGH);
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration > generation);
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(1), context.Controller.Snapshot.Position);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal(cueId, context.Session.SelectedCueId);
        Assert.Equal(trackId, context.Session.CurrentTrackId);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task QualityChangesDuringPlaybackKeepTheClockAndPlayingState()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration is not null);
        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        var before = context.Controller.Snapshot;
        Choose(UiTestActions.Find<ComboBox>(context.Window, "QualityCombo"), PreviewQuality.STANDARD);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
        Assert.Equal(before.Position, context.Controller.Snapshot.Position);
        Assert.Equal(before.PresentedGeneration, context.Controller.Snapshot.PresentedGeneration);
        Assert.Equal(new PixelSize(1280, 720), UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").MaximumPreviewSize);
    }

    [AvaloniaFact]
    public async Task QualityChangesDuringPlaybackKeepActualCanvasPixelsAdvancingWithoutTickRecomposition()
    {
        MainWindowTestContext? owner = null;
        await using var context = new MainWindowTestContext(
            videoSourceFactory: () => new PreviewTestSource(20, Enumerable.Range(0, 51).Select(index => index * 100L).ToArray()),
            videoConverterFactory: () => new UiComposedPreviewConverter(owner!.Session));
        owner = context;
        await context.OpenMediaAsync();
        context.Window.GetCommand(WorkbenchCommand.VIEW_PREVIEW).Execute(null);
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration is not null);
        var initialGeneration = context.Controller.Snapshot.PresentedGeneration;
        await context.Controller.RefreshPausedPreviewAsync();
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration > initialGeneration);
        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromMilliseconds(50));
        context.Session.Tick();
        Assert.Equal(new SKColor(0, UiComposedPreviewConverter.Green(20, PreviewQuality.LOW), 0), CanvasPixel(canvas));
        var firstSequence = canvas.PreviewSequence;
        context.Clock.Advance(TimeSpan.FromMilliseconds(10));
        context.Session.Tick();
        Assert.Equal(new SKColor(0, UiComposedPreviewConverter.Green(20, PreviewQuality.LOW), 0), CanvasPixel(canvas));
        Assert.Equal(firstSequence, canvas.PreviewSequence);

        Choose(UiTestActions.Find<ComboBox>(context.Window, "QualityCombo"), PreviewQuality.STANDARD);
        var changedQualitySequence = canvas.PresentedPreviewSequence;
        var observed = new List<SKColor>();
        var elapsedMilliseconds = 60;
        for (var step = 0; step < 20 && observed.Distinct().Count() < 4; step++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(100));
            elapsedMilliseconds += 100;
            context.Session.Tick();
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var snapshot = context.Controller.Snapshot;
            var actual = CanvasPixel(canvas);
            if (!snapshot.IsPresentedFrameCurrent || snapshot.PresentedFrameTime is not { } sourceTime)
            {
                continue;
            }

            var marker = checked((byte)(20 + sourceTime.Numerator * 10 / sourceTime.Denominator));
            var expected = new SKColor(0, UiComposedPreviewConverter.Green(marker, PreviewQuality.STANDARD), 0);
            if (actual == expected)
            {
                observed.Add(actual);
            }
        }

        Assert.Equal(4, observed.Distinct().Count());
        Assert.True(canvas.PresentedPreviewSequence > changedQualitySequence);
        Assert.Equal(canvas.PreviewSequence, canvas.PresentedPreviewSequence);
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
        Assert.Equal(new MediaTime(elapsedMilliseconds, 1000), context.Controller.Snapshot.Position);
        Assert.Equal(new PixelSize(1280, 720), canvas.MaximumPreviewSize);
    }

    private static SKColor CanvasPixel(EffectCanvasControl canvas)
    {
        var width = Math.Max(1, (int)Math.Ceiling(canvas.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(canvas.Bounds.Height));
        using var target = new RenderTargetBitmap(new(width, height), new(96, 96));
        using (var drawing = target.CreateDrawingContext())
        {
            canvas.Render(drawing);
        }

        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var board = canvas.ProjectRectangle;
        return pixels.GetPixel((int)(board.X + board.Width / 2), (int)(board.Y + board.Height / 2));
    }

    private static void Choose(ComboBox selector, PreviewQuality quality)
    {
        selector.SelectedItem = selector.Items.OfType<PreviewQualityChoice>().Single(choice => choice.Id == quality);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task DrainAsync(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!complete())
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
