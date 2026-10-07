using AegiNext.Desktop.Settings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewCatchupUiTests
{
    [AvaloniaFact]
    public async Task SustainedPresentationLagShowsAnOverlayWithoutCoveringNormalSkippedFrameGapsOrChangingQuality()
    {
        using var converter = new CatchupPreviewConverter();
        await using var context = new MainWindowTestContext(videoConverterFactory: () => converter);
        await context.OpenMediaAsync();
        await EventuallyAsync(() => context.ViewModel.Preview.HasFrame);
        context.Session.UpdatePreferences(value => value with { PreviewQuality = PreviewQuality.HIGH });
        var overlay = UiTestActions.Find<Border>(context.Window, "CatchingUpOverlay");
        converter.Block(2);
        try
        {
            await context.Controller.PlayAsync();
            await EventuallyAsync(() => context.Clock.ActiveTimerCount != 0);
            context.Clock.Advance(TimeSpan.FromMilliseconds(5100));
            await converter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Session.Tick();
            Assert.False(overlay.IsVisible);
            Assert.False(context.Controller.Snapshot.IsPresentedFrameCurrent);
            context.Clock.Advance(TimeSpan.FromMilliseconds(300));
            context.Session.Tick();
            Assert.True(overlay.IsVisible);
            Assert.False(context.Controller.Snapshot.IsPresentedFrameCurrent);
            Assert.Equal(PreviewQuality.HIGH, context.Session.Preferences.PreviewQuality);
            await context.Controller.PauseAsync();
            context.Session.Tick();
            Assert.False(overlay.IsVisible);
            Assert.Equal(PreviewQuality.HIGH, context.Session.Preferences.PreviewQuality);
        }
        finally
        {
            converter.Release();
        }
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
