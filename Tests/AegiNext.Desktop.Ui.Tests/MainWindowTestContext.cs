using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Views;
using AegiNext.Media.Playback;
using AegiNext.Media.Audio;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class MainWindowTestContext : IAsyncDisposable
{
    private readonly UiTestEnvironment environment = new();
    private readonly List<PreviewTestSource> sources = [];

    internal MainWindowTestContext(Func<string, int, MediaTime, CancellationToken, Task<AudioPlaybackSession>>? audioFactory = null,
        Func<PreviewTestSource>? videoSourceFactory = null, MediaTime? mediaStart = null,
        Func<string, CancellationToken, Task<VideoPreviewMedia>>? mediaProbe = null)
    {
        Window = new(present =>
        {
            Controller = new(mediaProbe ?? ((_, _) => Task.FromResult(new VideoPreviewMedia(0, mediaStart ?? MediaTime.Zero, new(20), audioFactory is null ? null : 1, VideoWidth: 1, VideoHeight: 1))),
                (_, _, position) => new(token =>
                {
                    token.ThrowIfCancellationRequested();
                    var source = videoSourceFactory?.Invoke() ?? new PreviewTestSource(1, 0, 5000, 10000, 15000, 20000);
                    sources.Add(source);
                    return source;
                }, Clock, externalPosition: position), () => new UiPreviewConverter(), DispatchAsync, present, audioFactory);
            return Controller;
        });
        Window.Show();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    internal MainWindow Window { get; }
    internal AegiNext.Desktop.Workspace.WorkbenchSession Session => Window.Session;
    internal AegiNext.Desktop.Workspace.WorkbenchViewModel ViewModel => Window.ViewModel;
    internal AegiNext.Desktop.Windowing.WorkbenchWindowRegistry WindowRegistry => Window.WindowRegistry;
    internal VideoPreviewController Controller { get; private set; } = null!;
    internal ManualPlaybackTimeProvider Clock { get; } = new();

    internal async Task OpenMediaAsync()
    {
        var path = Path.Combine(environment.DirectoryPath, "fixture.media");
        await File.WriteAllBytesAsync(path, new byte[20], TestContext.Current.CancellationToken);
        await Window.OpenMediaAsync(path, true);
        Assert.Equal(VideoPlaybackState.PAUSED, Controller.Snapshot.State);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Session.Details.Restore("All");
            Session.Details.Restore("Duration");
            Session.Details.Restore("LeadingDelay");
            Window.Close();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Window.IsVisible)
            {
                DiscardUnsavedDialogs(Window);

                Assert.True(DateTime.UtcNow < deadline, "Main window did not complete asynchronous close.");
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            await Window.DisposeAsync();
            Assert.All(sources, source => Assert.Equal(1, source.DisposeCount));
            Assert.All(sources.SelectMany(source => source.IssuedFrames), frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            environment.Dispose();
        }
    }

    private static void DiscardUnsavedDialogs(Avalonia.Controls.Window owner)
    {
        foreach (var window in owner.OwnedWindows.ToArray())
        {
            DiscardUnsavedDialogs(window);
            if (window is UnsavedProjectDialog dialog)
            {
                dialog.Close(2);
            }
        }
    }

    private static async Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
