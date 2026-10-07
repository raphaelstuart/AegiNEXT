# Video export

[English](export.md) · [简体中文](../zh-cn/export.md) · [All guides](README.md)

## Export a video

1. Open **Export** and choose Automatic, H.264, or HEVC.
2. Select CPU encoding (speed/CRF) or GPU encoding (bitrate), then choose source-audio copy, AAC, or no audio.
3. Click **Export** and choose a new MP4 or Matroska file.
4. Follow progress, cancel if needed, and inspect the finished output.

Export captures the project when started and runs in an independent `aegn-exporter` process. Later edits do not affect the job. Success atomically commits the new file; failure/cancellation cleans only its own temporary output. Existing destinations are rejected.

## Choose CPU or GPU

| Mode | Controls and scope |
|---|---|
| CPU, default | libx264/libx265, speed and CRF; required for HDR |
| GPU | 0.1–200 Mbps, default 8 Mbps; SDR H.264 8-bit or HEVC 10-bit |

macOS uses VideoToolbox; Windows tries supported NVENC/QSV/AMF backends. Missing hardware/driver/encoder reports an error. Disable GPU to use software encoding. GPU accelerates video encoding; subtitle composition still uses the linear high-precision pipeline.

## HDR and input requirements

Editing previews are SDR. PQ/HLG export reads original high-precision frames and uses software HEVC 10-bit; HDR H.264 and GPU HDR are rejected.

- PQ uses a 203-nit subtitle reference white by default. HLG uses 1000-nit, zero-black reference conditions.
- Complete static HDR10 mastering metadata is retained; MaxCLL/MaxFALL become unknown 0/0 after composition.
- Dynamic HDR such as Dolby Vision/HDR10+, changing/incomplete mastering, and unsupported color semantics are rejected.
- Input must have supported integer planar YUV, square pixels, and canvas dimensions matching visible video. VFR retains source presentation timestamps.
- Rotation, stereo, ICC, interlace, corrupt/dynamic formats, and negative timestamps unsupported by the muxing path report errors.

Copy audio retains packet content; AAC uses the selected bitrate. Container/timestamp incompatibilities report diagnostics rather than silently shifting the source timeline.

For ASS/SRT files, use [Subtitle editing](subtitle-editing.md). Package requirements and verification are in [Publishing](publishing.md).
