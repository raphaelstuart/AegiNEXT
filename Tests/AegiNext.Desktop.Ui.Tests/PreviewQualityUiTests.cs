using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Preview;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

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
