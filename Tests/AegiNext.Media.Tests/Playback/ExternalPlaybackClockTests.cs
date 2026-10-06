using AegiNext.Core.Timing;
using AegiNext.Media.Playback;

namespace AegiNext.Media.Tests.Playback;

public sealed class ExternalPlaybackClockTests
{
    [Fact]
    public async Task ExternalConsumptionOverridesWallTimeAndFailureContinuesFromItsLastPosition()
    {
        var clock = new ManualPlaybackTimeProvider();
        var gate = new Lock();
        MediaTime? external = MediaTime.Zero;
        var source = new FakeVideoFrameSource(0, 1000, 10000);
        await using var session = new VideoPlaybackSession(_ => source, clock, externalPosition: () =>
        {
            lock (gate)
            {
                return external;
            }
        });
        await session.OpenAsync();
        using var opened = await session.ReadPresentationAsync();
        await session.PlayAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(MediaTime.Zero, session.Snapshot.Position);
        lock (gate)
        {
            external = new(1, 2);
        }

        Assert.Equal(new MediaTime(1, 2), session.Snapshot.Position);
        lock (gate)
        {
            external = null;
        }

        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(new MediaTime(3, 5), session.Snapshot.Position);
        await session.PauseAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(new MediaTime(3, 5), session.Snapshot.Position);
    }
}
