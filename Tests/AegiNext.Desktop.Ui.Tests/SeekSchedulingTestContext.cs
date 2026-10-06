using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Views;
using AegiNext.Media.Playback;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class SeekSchedulingTestContext : IAsyncDisposable
{
    private readonly UiTestEnvironment environment = new();

    internal SeekSchedulingTestContext()
    {
        Window = new(present =>
        {
            Controller = new((_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), VideoWidth: 1, VideoHeight: 1)),
                (_, _) => new(token =>
                {
                    token.ThrowIfCancellationRequested();
                    return Source;
                }), () => new UiPreviewConverter(), DispatchAsync, present);
            return Controller;
        });
        Window.Show();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    internal MainWindow Window { get; }
    internal VideoPreviewController Controller { get; private set; } = null!;
    internal BlockingPreviewSource Source { get; } = new();

    internal async Task OpenMediaAsync()
    {
        var path = Path.Combine(environment.DirectoryPath, "blocked-seek.media");
        await File.WriteAllBytesAsync(path, new byte[20], TestContext.Current.CancellationToken);
        await Window.OpenMediaAsync(path, true);
        Assert.Equal(VideoPlaybackState.PAUSED, Controller.Snapshot.State);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Window.Close();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Window.IsVisible)
            {
                foreach (var dialog in Window.OwnedWindows.OfType<UnsavedProjectDialog>().ToArray())
                {
                    dialog.Close(2);
                }

                Assert.True(DateTime.UtcNow < deadline, "Main window did not complete asynchronous close with a pending seek.");
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            await Window.DisposeAsync();
            Assert.Equal(1, Source.DisposeCount);
            Assert.All(Source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            environment.Dispose();
        }
    }

    private static async Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
