using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class PreviewQualityAudioStateTests
{
    /// <summary>真实偏好更新流程在暂停或播放中切换画质，不改变静音、保存音量和输出增益。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangingPreviewQualityPreservesAudioMuteVolumeAndPlaybackState(bool muted, bool playing)
    {
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), output, position))));
        await context.InitializeAsync();
        var session = context.Session;
        var controller = session.Controller;
        await controller.OpenAsync("quality-audio-state.mkv");
        await controller.SeekAsync(new MediaTime(3, 4));
        using var presentationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (controller.Snapshot.PresentedGeneration is null || controller.Snapshot.PresentedAtPosition != new MediaTime(3, 4))
        {
            await Task.Delay(1, presentationTimeout.Token);
        }
        session.ViewModel.Preview.Volume = 0.35;
        session.ViewModel.Preview.IsMuted = muted;
        if (playing)
        {
            await controller.PlayAsync();
        }
        var position = controller.Snapshot.Position;
        var generation = controller.Snapshot.PresentedGeneration;
        var document = context.Editor.Snapshot;

        session.UpdatePreferences(value => value with { PreviewQuality = PreviewQuality.HIGH });

        if (!playing)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (controller.Snapshot.PresentedGeneration is not { } current || generation is not { } previous || current <= previous)
            {
                await Task.Delay(1, timeout.Token);
            }
        }
        session.Tick();
        Assert.Equal(PreviewQuality.HIGH, session.Preferences.PreviewQuality);
        Assert.Equal(0.35F, session.Preferences.Volume);
        Assert.Equal(0.35, session.ViewModel.Preview.Volume, 6);
        Assert.Equal(muted, session.ViewModel.Preview.IsMuted);
        Assert.Equal(muted, controller.Snapshot.IsMuted);
        Assert.Equal(muted ? 0 : 0.35F, output.Gain);
        Assert.Equal(position, controller.Snapshot.Position);
        Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Null(session.LastError);
        Assert.Same(document, context.Editor.Snapshot);
        session.ViewModel.Preview.IsMuted = false;
        Assert.Equal(0.35F, output.Gain);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
