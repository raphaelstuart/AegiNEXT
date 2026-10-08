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

Binding media starts an `AudioAnalysisSession` that builds complete waveform and spectrum pyramids in the project's `caches/audio/<identity>/`. `data.bin` stores uncompressed binary blocks; `index.bin` stores the version, exact rational timeline, level indexes, and SHA-256 checksums. Identity includes the algorithm version, stream index, file size and modification time, and up to 64 KiB each from the beginning, middle, and end. Moving media preserves identity. Complete caches survive reopening; cancelled, failed, or abandoned temporary builds cannot become cache hits.

Analysis uses a fixed grid: 48 kHz mono waveform min/max with a finest persistent level of 512 samples; a 63-tap FIR low-pass to 16 kHz; and 1024-point Hann FFT, 256-sample hop, and 128 logarithmic frequency rows. Normal waveform zoom and every spectrum zoom read existing levels through binary indexes. Coarse spectra select centers on the absolute media grid. Only waveform requests finer than 512 samples open a separate local decoder with a bounded PCM LRU. Viewports never compute FFTs or consume playback audio.

`AudioAnalysisCacheBuilder` decodes sequentially through one source and runs segment FIR/FFT in parallel. The default segment contains 196608 samples (4.096 seconds), with up to four DSP workers. Each segment filters once and reuses overlapping FFT windows. Batch working storage is capped at 48 MiB, and the default combined read-cache cap is 16 MiB. PCM is not persisted. Finished batches publish in order; unfinished intervals remain pending. Hiding layers cancels viewport reads; replacing media or closing cancels and drains the build. Batch checkpoints can cooperatively release the task slot so saving and media replacement can proceed even with a one-slot limit.

After Save As succeeds, `AudioCacheMigrationTask` copies the complete cache in the background. A running build records the latest destination and migrates after completion. Saving waits for neither generation nor copying. Migration owns a destination writer lease and switches data and index together. Cancellation or copy failure retains the original read path; another save can retry.

## Verify

```powershell
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoPreview'
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Analysis'
```

Native cases need the switches and tool paths in [Building](building.md). Test actual PTS/pixels, seeking, supersession, cancellation, frame lifetime, and faults. Silent system-device tests do not establish speaker latency; Headless presentation does not establish native HDR or GPU acceptance.
