# Sequential video decoding

[English](video-decoding.md) | [简体中文](zh-CN/video-decoding.md)

native/decoder builds aeginext_decode; Media/Decoding supplies managed contracts/SafeHandle ownership. It depends only on pinned FFmpeg, not libplacebo/MoltenVK/windows. Original Step 1.6 added local explicit-stream sequential reading, EOF drain, cancellation, and retained frames; Step 1.7 added keyframe seek/exact selection. Current playback/color/export integration is separate: [workbench](workbench.md).

## Decode modes and missing color tags

Preview and export statically link the same `native/shared` media core while owning independent sessions. It shares demux, receive-first decoding, seek/EOF/cancellation, hardware readback and color interpretation with pinned FFmpeg 9.0.2. Preview remains SDR BGRA; export retains its high precision composition path.

Settings → Media → Preview decoder offers Auto, CPU and GPU. Auto prefers VideoToolbox on macOS or D3D11VA on Windows and may fall back before the first delivered frame for unavailable hardware, negotiation or initial readback failures. Required GPU verifies actual hardware and rolls back the setting, position and playback state on failure. Cancellation, I/O, allocation failures, corrupt input and failures after frame delivery do not trigger fallback. Older preferences default to Auto. Export has its own Auto-default `DecodeMode`; preview preferences do not change it. Encoder selection remains `EncodingMode`.

Hardware v1 covers opaque 4:2:0 H.264 8-bit and HEVC 8/10-bit. Readback preserves NV12/P010 and all frame properties/side data without reducing 10-bit to NV12; cache entries are CPU accessible. VideoToolbox confirms actual acceleration through its public session property; D3D11VA uses modern D3D11 frames. Interactive software decoding uses one thread, offline export at most four, and hardware uses one outer thread.

Frame size and crop describe the actual decoded backend layout. For example, a software decoder may expose 1088 coded rows with an 8-row crop, while VideoToolbox exposes 1080 visible rows with no crop. Readback preserves the hardware frame's actual properties and does not invent unavailable padding. Color defaults use visible dimensions after crop; equivalence checks compare visible pixels, timing, bit depth and color/HDR metadata rather than requiring identical coded layouts.

Raw `VideoFrameInfo` continues to report missing tags. Native `ResolvedVideoColor` supplies effective parameters and an inferred-field mask. Only actual missing sentinels are filled: RGB/yuvj defaults to full range, ordinary YUV to limited; a missing matrix first follows known primaries, otherwise HD uses BT.709 and SD BT.601; missing primaries follow the effective matrix and PAL/NTSC height; missing transfer uses sRGB for full-range RGB/YUV or BT.709/SMPTE170M for limited YUV; missing subsampled chroma uses LEFT/CENTER for limited/full range. PQ, HLG, BT.2020 or HDR side-data evidence requires complete supported tags instead of SDR defaults.

Frame ABI remains 1 with appended options, actual session info, core version and color-resolution entries. Export request ABI is 3, size 80 bytes. Stale libraries, missing capabilities or a core version mismatch require rebuilding instead of calling an incompatible layout. Final mux validation uses effective encoded color metadata, so absent source tags do not reject correctly encoded output.

## Build and platforms

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Decoder -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Decoder -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Managed
```

Decoder builds native only; Managed copies matching RID/Configuration output. Missing checks fail unless installation explicitly requested. macOS uses Homebrew; Windows x64 uses Scoop main/mingw/cmake/ninja/ffmpeg-shared. A CLI-only distribution cannot replace headers/import libraries/shared runtime. See [SDK selection](building.md).

Outputs: artifacts/native/osx-<arch>/<Configuration>/libaeginext_decode.dylib and win-x64 equivalent aeginext_decode.dll, with selected SDK DLLs. Native/All still mean optional macOS HDR, excluding Decoder.

The original milestone ran only on Apple Silicon macOS 27; later Windows native/worker evidence is [recorded separately](README.md#implementation-evidence). A macOS 14 target cannot lower local FFmpeg's 27 minimum. Minimum OS/complete package/licenses need their own tests. Linux Decoder is deferred.

## Timing and facts

- Open accepts the container's absolute stream index, not ordinal first-video position; use probe facts.
- ReadFrame returns null only for normal EOF; faults/cancel throw. Receive buffered frames before sending packets; demux EOF drains through decoder EOF, retaining final B-frames.
- Immutable VideoFrameInfo separates raw PTS, best-effort, duration, effective time base, AVFrame base, stream base. PTS/duration use effective frame base; best-effort uses stream base. Unknown is null, never fps/frame-number/zero.
- Preserve format/component depths/size/SAR/crop/key/corrupt/interlace/decode flags.
- Frame color retains enum numbers/names for range/matrix/primaries/transfer/chroma/alpha mode; unknown names are null, not guessed.
- Mastering/content-light come from actual frame side data; existence/primaries/luminance are independent, partial fields stay unknown, zero CLL/FALL unknown, ratios exact.
- SideDataTypes means types present, not parsed/reemittable payload guarantees. Dolby Vision/HDR10+ are unsupported here.

## Pixels and resources

DecodedVideoFrame owns an independent AVFrame reference surviving later reads/decoder disposal. Managed Info survives frame disposal; plane access/copy then throws ObjectDisposedException.

GetPlaneInfo returns signed stride/row bytes/height. CopyPlane produces tight independent bytes, valid pixels only, no padding; negative stride keeps logical row order, single-row palette can have zero stride. Caller mutation never changes native frame. Native verifies plane storage inside actual AVBuffers.

Copy does not crop/convert/depth-reduce/tone-map. Raw YUV/RGB requires its color facts; it is not display-ready RGBA. Preserving 10-bit alone does not prove HDR composition/export.

Reads are synchronous and belong on workers. Open/read/dispose serialize per instance. Cross-thread Cancel sets native atomic state checked by I/O callbacks/decode loops; cooperative cancellation does not preempt arbitrary codec calculations. Started cancel is terminal; pre-canceled tokens throw before entering native and do not cancel an existing session.

Managed Dispose requests cancel, awaits running read, releases SafeHandle. Each returned frame remains caller-owned. Raw ABI consumers must never destroy concurrently with reading.

```csharp
using var decoder = FfmpegVideoDecoder.Open(localFilePath, videoStreamIndex, cancellationToken);
while (decoder.ReadFrame(cancellationToken) is { } frame)
{
    using (frame)
    {
        var timing = frame.Info.PresentationTimestamp;
        var layout = frame.GetPlaneInfo(0);
        var pixels = frame.CopyPlane(0);
    }
}
```

## ABI and versions

ABI 1 uses exact-size/version fixed-width structs, UTF-8 paths/errors, Windows cdecl. C/C++ static_assert and managed Marshal tests check layout. Exceptions become error codes; SafeHandle owns release.

Step 1.7 appended an_decode_features/an_decoder_get_time_base/an_decoder_seek without changing structs. SEEK requires both entries; open checks capabilities and explicitly asks to rebuild older libraries despite ABI 1 matching.

Compiled/runtime versions must match ffmpeg-toolchain.json: FFmpeg 9.0.2, avformat/avcodec 63.1.102, avutil 61.1.102, allowlisted Gyan suffix. GetBackendInfo loads and checks actual runtime; build checks are not substitutes.

## Seeking and frame selection

StreamTimeBase queries selected stream. SeekToKeyFrame floors rational time to it, calls av_seek_frame BACKWARD, and preserves absolute time. Success flushes codec/pending/scratch/demux/drain/EOF. Normal EOF can reseek; cancellation/actual seek failure cannot recover. Undefined timestamp sentinel/overflow reject before calling. [Pinned FFmpeg contract](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libavformat/avformat.h#L2255-L2297)

Keyframe seek alone is not exact display selection. VideoFrameNavigator owns factory/bounded lookahead/raw interval cache, decodes actual next PTS, and selects `Time <= target < NextFrameTime`; exact next start selects next. VFR comparisons remain rational.

For duplicate PTS choose the last decoded frame and dispose others. Missing raw PTS/base or presentation-time reversal throws InvalidDataException without best-effort substitution/sorting/fps guesses. Last frame has null next and ReachedEnd true, not fabricated duration.

First seek confirms actual first time. Before-first reuses known first or reopens; after-last returns last. If demux lands after target or at EOF, reopen and scan from beginning to confirm, rather than pretending a late landing is first. Fallback can be expensive; no complete index exists.

PositionedVideoFrame uniquely owns its IVideoFrame. Later read/seek/navigator-close does not reclaim handed-out frames; navigator releases only lookahead/cache/decoder. Factories return newly opened decoders. Explicit cancel stops current native work; ordinary pause/replacement uses nonterminal playback rules: [playback](video-playback.md).

## Scrubbing cache

Bounded LRU reuses decoded intervals in either direction without repeated GOP decode, default 120 frames/256 MiB. Budget per plane: max(abs(stride), rowBytes) × height; oversized single frames are not cached. Decoder internals/conversion/external leases are outside this budget. No pixel copies/SDR conversion; depth/color/HDR remain.

After cache hit, sequential playback follows logical next PTS and reconnects actual decoder, not physical read head. Uncached targets within 250 ms ahead of lookahead sequentially decode; others keyframe-seek/preroll. Generation replacement checks between native reads are nonterminal, unlike sticky Cancel. Close/explicit cancel retain terminal semantics.

## Tests

Ordinary Media tests explicitly skip native cases unless enabled. Once enabled, missing libraries/tools/version errors fail:

```powershell
$env:AEGINEXT_RUN_DECODER_TESTS = '1'
# Use actual tools from the selected shared SDK.
$env:AEGINEXT_FFMPEG_PATH = '/opt/homebrew/opt/ffmpeg/bin/ffmpeg'
$env:AEGINEXT_FFPROBE_PATH = '/opt/homebrew/opt/ffmpeg/bin/ffprobe'
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Decoding'
```

CTest covers planes/negative stride/odd 10-bit chroma/palettes/time/partial HDR/ABI/lifetime. Managed fixtures generate short PQ/HLG/BT.2020 SDR HEVC with audio first, B-frames, VFR; compare all PTS to FFprobe and decoded bytes to FFmpeg, plus retained frames/copy/cancel/errors. Historical [results](README.md#implementation-evidence) do not by themselves establish realtime playback or HDR export; current platform evidence has its own dated scope.
