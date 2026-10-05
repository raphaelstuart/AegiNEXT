# Video export and HDR contracts

[English](export.md) | [简体中文](zh-CN/export.md)

The workbench exports an immutable snapshot in an independent AegiNext.ExportWorker process sharing Core evaluation and Rendering. UI options cover automatic/H.264/HEVC, CPU/GPU video encoding, source-audio copy/AAC/no audio, progress/cancel, and MP4/Matroska.

Targets must be new files. Worker encodes in a private directory beside the destination; success atomically commits, failure/cancellation removes only this job's temporary output. Later edits do not affect a running snapshot. Packages must include worker apphost/DLL/deps/runtimeconfig/managed dependencies and matching native libraries, not just the desktop executable.

## CPU and GPU

CPU defaults to libx264/libx265 with speed/CRF. GPU mode uses 0.1–200 Mbps, default 8 Mbps. Modes retain separate drafts; hidden-mode fields do not block export. Progress/result/log report the actual encoder. Hardware completion verifies worker encoder identity; old worker/missing identity/software results never commit as successful hardware output.

macOS uses VideoToolbox without software fallback. Windows initializes available NVENC/QSV/AMF backends. Missing driver, VM exposure, SDK encoder, or initialization yields specific diagnostics rather than CPU fallback; disable GPU to request software.

Only video encoding is accelerated; decode/linear subtitle composition retain their paths. GPU supports SDR H.264 8-bit/HEVC 10-bit. HDR metadata preservation is accepted only on software HEVC; GPU HDR explicitly rejects and directs the user to CPU.

## Color pipeline

Decode preserves integer YUV, PTS/depth/range/primaries/transfer/matrix/supported metadata. Native export converts to explicit linear floating brightness, transforms/composes linear BT.709 F16 overlays, then returns to source encoding. Uncovered transparent pixels preserve source code values at composition; HDR background never passes through preview BGRA8.

- SDR supports H.264 8-bit or HEVC 10-bit.
- PQ/HLG Auto uses HEVC 10-bit; explicit HDR/H.264 rejects.
- PQ uses absolute EOTF; subtitle reference white defaults to 203 nits. HLG uses 1000-nit/zero-black OOTF reference conditions.
- Complete static HDR10 mastering is retained. MaxCLL/MaxFALL become unknown 0/0 after composition rather than unverified source statistics.
- Dolby Vision/HDR10+/other dynamic HDR, changing or incomplete mastering reject.

Retained model masks/groups/blur stay F16. Normal/multiply/screen/add/overlay/darken/lighten/difference operate in linear space; additive values above reference white survive. UI color display limits do not rewrite untouched extended values.

## Supported inputs and rejection

Require explicit color, integer planar YUV 8–16-bit, supported known chroma location, square pixels, and project canvas equal to visible video. Source per-frame timestamps go to the encoder; VFR stays VFR. Rotation/stereo/ICC/interlace/corruption/dynamic formats and unsupported semantics reject. Negative PTS that NUT cannot represent fails without origin reset.

Audio copy retains packet content; AAC uses selected bitrate; no-audio removes tracks. Preserving timestamps does not promise every start combination is supported by every muxer; failures expose diagnostics.

## Recorded verification

Native numeric/worker tests cover SDR/PQ/HLG/VFR/audio copy/AAC/cancel/nonoverwrite/HDR-H.264 rejection. PQ 1000/4000-nit highlight readback codes are 723/855. A 203-nit white subtitle at 50% alpha over 1000-nit background reads 675 (theory ≈674.52); HLG under its reference conditions reads 872.

Tests inspect output pixels/metadata, not HDR tags alone. CPU/GPU/platform scope is in [the checkpoint](checkpoints/export-title-colors.md). Real HDR display appearance and physical Windows hardware require separate evidence.
