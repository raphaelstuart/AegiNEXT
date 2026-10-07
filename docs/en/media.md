# Media integration

[English](media.md) · [简体中文](../zh-cn/media.md) · [All guides](README.md)

## Set up and locate the adapter

Build `Workbench` for matching RID/configuration native libraries; see [Building](building.md). Development runs use absolute `AEGINEXT_FFPROBE_PATH`/`AEGINEXT_FFMPEG_PATH` or PATH; invalid explicit paths fail. Packages use their own tools.

| Location | Responsibility |
|---|---|
| `src/AegiNext.Media/Probing` | FFprobe tool identity, bounded processes, and immutable media facts |
| `src/AegiNext.Media/Decoding` | Decoder/frame contracts, native handles, and exact frame navigation |
| `src/AegiNext.Media/Playback` | Raw-frame scheduling and presentation ownership |
| `src/AegiNext.Media/Preview` | Independent SDR conversion |
| `src/AegiNext.Media/Audio`, `Analysis` | Output clocks and viewport audio analysis |
| `src/AegiNext.Desktop/Controllers`, `Workspace` | UI delivery, transport, calibration, and analysis coordination |
| `native/shared` | Shared preview/export demux, decode, seek, and color interpretation |

## Probe and decode

Use `FfprobeMediaProbe` for existing local files. It validates tool identity, invokes without a shell, drains both pipes, and bounds timeout/output. Preserve all stream indices, raw timestamps/time bases, color/HDR facts, and unknown values; reported frame rates do not prove CFR.

Open the selected absolute stream index with `FfmpegVideoDecoder`. Receive buffered frames and drain EOF. A returned frame owns an independent reference; later reads or decoder disposal cannot reclaim it. Dispose every frame explicitly. Plane copies contain valid pixels in logical row order, without cropping or color conversion.

Seek to a keyframe, then select the actual-PTS interval `Time <= target < NextFrameTime`. VFR remains rational. Missing/regressing PTS rejects; duplicate PTS selects the last decoded frame. Cache decoded intervals with bounded frame/byte budgets; cache hits must reconnect sequential playback to the logical next frame.

Auto decoding prefers VideoToolbox on macOS or D3D11VA on Windows and can fall back before first delivery. Required GPU verifies acceleration and reports failure; cancellation, corrupt input, and later errors do not trigger silent fallback. Hardware readback preserves depth and frame metadata.

Missing SDR color tags use explicit shared resolution rules. Raw facts remain unchanged, while resolved color records inferred fields. HDR evidence requires complete supported tags. Preview decoder preference and export decode/encode choices are independent.

## Keep one time source

Default audio output uses CoreAudio on macOS and WASAPI on Windows: 48 kHz stereo float PCM, about 200 ms prefill, at most 250 ms queued. Device presentation position maps to consumed media samples; SYSTEM time does not subtract a guessed queue/buffer delay.

Additional delay calibration is keyed by device, backend, sample rate, and channels. Device-clock loss freezes timing until replacement; F8/F9 cannot use an unavailable clock. Audio gaps/short tracks use silence. Video without audio uses a monotonic clock; explicit SDL output is ESTIMATED.

Playback results carry generations. New commands supersede stale deliveries; consumers check identity immediately before presentation. Pause/replacement cancels obsolete work without terminally canceling the shared decoder. Close requests native cancellation, drains work, and releases undelivered resources; handed-out frames remain consumer-owned.

## Compose preview and analyze audio

SDR conversion borrows the raw frame and produces independent, opaque, top-down sRGB BGRA8. Export never uses these display pixels. Composition keeps the corresponding uncomposed background to avoid double subtitles or wrong-frame reuse.

Controller results check request, media time, project revision, and quality. Select preview quality in the workbench; interaction uses a temporary cap. Measure presented frames and dispatch latency separately from decode duration, especially for long GOP, high resolution, and repeated seeks.

Analysis uses an independent fixed grid: 48 kHz mono waveform min/max, plus low-pass 16 kHz spectrum with 64 ms FFT and 16 ms hop. Request viewport tiles with bounded prefetch/cache, share PCM across layers, and preserve source time. Color/playhead refreshes reuse geometry; hiding/media replacement/close cancels obsolete analysis without consuming playback audio.

## Verify

```powershell
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoPreview'
```

Native cases need the switches and tool paths in [Building](building.md). Test actual PTS/pixels, seeking, supersession, cancellation, frame lifetime, and faults. Silent system-device tests do not establish speaker latency; Headless presentation does not establish native HDR or GPU acceptance.
