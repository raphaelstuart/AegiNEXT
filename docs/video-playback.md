# Video playback foundation

[English](video-playback.md) | [简体中文](zh-CN/video-playback.md)

Media/Playback is UI-independent and uses original Media/Decoding frames with exact seek. Its original Phase 1 / Step 1.7 scope was raw-frame scheduling, without display conversion, speed/loop controls, or audio clock. Current Desktop integration and audio behavior are described in [workbench](workbench.md); this page specifies the underlying contract.

## Session and commands

VideoPlaybackSession receives a frame-source factory, optional TimeProvider, and queue capacity. One background loop executes factory/read/seek. OpenAsync(path, streamIndex, ...) is the local-file convenience path; factory injection supports other sources/deterministic tests.

| API | Completion semantics |
|---|---|
| OpenAsync | Open source, select first frame, pause; empty input ends. |
| PlayAsync | Anchor clock at exact current media position; EOF does not rewind. |
| PauseAsync | Freeze position captured at submission, seek and deliver that picture. |
| SeekAsync | Pause/select target frame; result includes requested/selected times, next boundary, generation, before-first/EOF, without transferring frame ownership. |
| ReadPresentationAsync | Return one disposable presentation; wait when empty, null for current end/close, propagate faults. |
| Snapshot | Immutable state/position/frame-time/generation/fault. |
| CloseAsync / DisposeAsync | Terminal cancel, wake waiters, release undelivered frames/source, await loop; idempotent. |

Pause/ordinary seek do not call terminal decoder Cancel. A command token withdraws only work not started; running work still returns its result without aborting shared source. Closing stops native work. Consumer token cancels only its wait.

Newer Pause/Seek advances generation and clears undelivered stale frames. In-flight old seeks recheck after native return, dispose their results, and complete canceled. If executed source reads cannot roll back, reseek synchronizes its cursor; replacement never permanently cancels the decoder.

Seek retains a requested interior position: target 50 ms can display frame starting 40 ms, and resume starts at 50 ms. Before-first/confirmed-end targets clamp to boundary frames. No actual next PTS exists for the last frame, so stop at its start rather than guessing duration from fps.

## Time and scheduling

Construct rational position from monotonic timestamp delta / TimeProvider.TimestampFrequency plus anchor; never accumulate expected timer ticks or use wall-clock dates. Only waits quantize to TimeSpan with CEILING; on wake recheck actual media time. [TimeProvider contract](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider.gettimestamp?view=net-10.0)

Frames use actual-PTS half-open intervals. Exact next start chooses next frame; late loops dispose expired frames and choose current interval. Pause fixes position; resume establishes a new anchor. VFR/duplicate/missing PTS rules are in [decoding](video-decoding.md).

## Queue and ownership

Capacity defaults to 2. Full queues release oldest undelivered frame, never block the producer, leaving seek/close responsive. Reads also discard deliveries beyond true next boundaries.

Session owns queued VideoPresentation; successful consumers become sole disposal owners. Close never reclaims already handed-out frames. Consumers must verify Generation against current Snapshot immediately before presentation; internal stale rejection cannot prevent an already consumed old frame writing later.

Null means current end/close, not a permanently completed unreusable channel. Seek after EOF can deliver again. Source errors save fault, end the loop, reclaim internals, and never masquerade as EOF.

```csharp
await using var session = await VideoPlaybackSession.OpenAsync(localFilePath, videoStreamIndex);
var result = await session.SeekAsync(targetMediaTime);
using var presentation = await session.ReadPresentationAsync();
if (presentation is not null && presentation.Generation == session.Snapshot.Generation)
{
    var rawFrame = presentation.PositionedFrame.Frame;
    var timestamp = presentation.PositionedFrame.Time;
}
```

This API delivers raw media control/frames. YUV planes are not directly displayable Avalonia pixels, and SDR derivatives are never HDR export inputs.

## Verification scope

Deterministic controlled sources/blocking seek/manual clocks cover boundaries/pause/resume/slow consumers/catchup/replacement/cancel/fault/disposal. Real multi-GOP/B-frame CFR/VFR/nonzero-start fixtures compare FFprobe timing/FFmpeg pixels and delivery/seek/close wiring. Historical results are in [the original checkpoint](README.md#implementation-evidence); later Windows/audio/export evidence is separately dated. Long-file indexing, realtime performance, AV synchronization, desktop interaction, and minimum OS need appropriate acceptance beyond this module.
