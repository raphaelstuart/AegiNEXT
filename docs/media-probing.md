# Media probing

[English](media-probing.md) | [简体中文](zh-CN/media-probing.md)

The FFprobe adapter reads local container/all-stream facts as a production read-only input boundary. Decode/preview/export share Core contracts. Original Step 1.5 had no desktop/playback/encoding integration; current product integration is documented in [Quick Start](quick-start.md).

## Dependencies and invocation

Media embeds src/AegiNext.Media/Probing/ffmpeg-toolchain.json, shared with scripts. Lock FFmpeg/FFprobe 9.0.2, avutil 61.1.102, avcodec/avformat 63.1.102; accept plain release or allowlisted Gyan release. Other versions fail without upgrade/downgrade/PATH fallback.

```powershell
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -InstallDependencies
```

Managed build needs no media tools. Explicit WithMediaTools checks FFprobe/FFmpeg; only FFprobe is invoked by probing, while FFmpeg generates integration fixtures and supports later work. Installation requires the explicit switch using Homebrew/Scoop; see [building](building.md).

Supply the real absolute executable, not a Scoop shim. Development uses Homebrew paths or `(scoop prefix ffmpeg)/bin/ffprobe.exe`; complete current packages supply RID-specific local tools and licenses, described in [publishing](publishing.md). Tool-package/download/signing work was outside the original probing milestone.

```csharp
var probe = new FfprobeMediaProbe(new FfprobeOptions(ffprobeExecutablePath));
var report = await probe.ProbeAsync(mediaFilePath, cancellationToken);
var streams = report.Asset.Streams;
```

First validate tool identity, then probe media. Media output repeats versions and before/after SHA-256 checks confirm configured binary identity. Both compiled and runtime packed-library versions participate; reports retain configured path/hash/versions/stderr. Identity hash does not prove release provenance or dependency signatures.

## Facts and unknown values

Core has no JSON/process/FFmpeg dependency. Immutable snapshots with init properties do not become validated merely by external construction; adapters validate input.

| Data | Contract |
|---|---|
| Streams | All absolute indices, codec types/names/tags/dispositions, attached pictures, multiple defaults, noncontiguous indices; caller chooses tracks. |
| Time | Per-stream raw PTS/time base/duration ticks; StartTimestamp requires both PTS and base. Audio/video are not independently zeroed. |
| Reported seconds | Decimal text converts directly to rational MediaTime, separate from raw ticks; reported duration can be an estimate. |
| Ratios | Exact normalized MediaRatio; SAR/r_frame_rate/average are separate. Equal rates do not establish CFR or replace PTS. |
| Color | Preserve original range/matrix/transfer/primaries/chroma strings; unknown names are not BT.709. IsPq/IsHlg are signaling, not HDR10 compliance. |
| HDR metadata | Stream-level mastering chromaticity/cd/m² and CLL/FALL permit missing fields. Zero CLL/FALL is unspecified, not fabricated; metadata is not render reference white/pixel peak. |
| Video/audio | Size/format/reported bit depth/SAR/display matrix/rotation; audio rate/channels/layout without inferring layout from count. |

Missing/N/A/undefined-PTS sentinels/applicable unknown ratios are null. Malformed/duplicate-index/duplicate-field/illegal-sign/unrepresentable numbers fail. Numeric strings cap at 256 characters to bound BigInteger work.

## Process and coverage boundaries

- Existing local files only; FFprobe protocols restricted to file, not network.
- ArgumentList, no shell; stdin closed and stdout/stderr concurrently drained.
- Default 30-second invocation timeout, 16 Mi characters stdout, 64 Ki stderr. Cancel/timeout/overflow terminates a running process tree and awaits the directly started FFprobe. Wrappers that spawn background descendants then exit are unsupported; full group/Job ownership is separate work. Nonzero exit fails even with partial JSON.
- Probesize 10 MiB/analyzeduration 10 seconds; no full show_frames/show_packets/count. This is bounded probing, not a complete index.
- Absent stream HDR data means only this report omitted it; frame/dynamic data may differ. Unknown side-data types retain names, not full payload models.
- Generated PQ/HLG/BT.2020 fixtures prove input-fact reading, not output brightness/export quality.

Local FFmpeg fixtures showed output/x265 VUI flags alone could leave color unknown; setparams on encoder-input frames established expected signaling. Actual media also had mastering/CLL absent at stream level but present on the first frame. Thus decode/composition/encoding must propagate frame facts and validate output by redecoding, not command flags alone. References: [FFprobe](https://ffmpeg.org/ffprobe.html), [AVStream](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/avformat.h), [HDR metadata](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/mastering_display_metadata.h).

## Focused verification

Real tools explicitly skip unless configured. Check the toolchain, then provide real paths:

```powershell
# Windows can use Join-Path (scoop prefix ffmpeg) 'bin/ffprobe.exe'.
$env:AEGINEXT_FFPROBE_PATH = (Get-Command ffprobe).Source
$env:AEGINEXT_FFMPEG_PATH = (Get-Command ffmpeg).Source
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~MediaRatioTests'
```

Tests generate tiny 10-bit HEVC/PCM fixtures with Unicode/space/quote/shell-character paths, log reports, and delete them. AegiNext.Media.TestHost supplies deterministic real child processes for arguments/dual pipes/limits/cancel/timeout, and is not a desktop dependency/release component.
