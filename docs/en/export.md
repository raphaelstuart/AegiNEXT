# Video export

[English](export.md) · [简体中文](../zh-cn/export.md) · [All guides](README.md)

## Export a video

1. Open **Export** and choose Automatic, H.264, or HEVC.
2. Select CPU or GPU encoding, speed, and quality mode. CPU supports CRF or Mbps; GPU uses Mbps. In Mbps mode, choose VBR or CBR.
3. Choose source-audio copy, AAC, or no audio. AAC has a separate bitrate control.
4. Click **Export** and choose a new MP4 or Matroska file.
5. Follow progress, cancel if needed, and inspect the finished output.

Export captures the project when started and runs in an independent `aegn-exporter` process. Later edits do not affect the job. Success atomically commits the new file; failure/cancellation cleans only its own temporary output. Existing destinations are rejected.

## Choose CPU or GPU

| Mode | Controls and scope |
|---|---|
| CPU, default | libx264/libx265; CRF 0–51 (default 20), or VBR/CBR 0.1–200 Mbps; required for HDR |
| GPU | VBR/CBR 0.1–200 Mbps (default 8 Mbps); SDR H.264 8-bit or HEVC 10-bit |

Lower CRF values usually increase quality and size. Mbps is a target video bitrate, rounded down to 1 kbps precision. VBR varies with content; CBR uses a constant target and a constrained buffer. Measured bitrate for a short clip or individual frame need not equal the requested value exactly. The current internal VBR peak is twice the target; CBR minimum and maximum equal the target, and the buffer holds two seconds at the peak rate.

There are nine speed presets from ultrafast to veryslow. Software encoding uses the corresponding x264/x265 preset; hardware backends map these to supported speed settings.

macOS uses VideoToolbox; Windows tries NVENC/QSV/AMF candidates. QSV is currently rejected because its initialized rate-control mode cannot be confirmed through the public FFmpeg API. Missing hardware, drivers, encoders, or the requested rate mode reports an error without switching to CPU or another mode. Disable GPU to use software encoding. GPU accelerates video encoding; subtitle composition still uses the linear high-precision pipeline.

## Manage encoding presets

In **Settings → Encoding presets**, add or duplicate a preset, edit its name and encoding parameters, then save. Editing and saving an existing preset updates that preset. **Capture current settings** copies the active workbench encoding configuration into a new, independent draft. When changing selection, leaving the page, or closing Settings, pending changes can be saved, restored, or kept by cancelling the transition.

The list supports multiple selection. **Export** writes the selected saved presets into one `.aegiexports` file. **Import** accepts multiple exchange files, each of which may contain multiple presets. Invalid versions, parameters, or conflicting identities/names reject the whole import without partial changes. Exchange files contain no project, media, output, or tool paths.

Select a preset in the **Export** panel to apply it directly. Library edits and imports refresh the available list while keeping current parameters and unfinished inputs until you explicitly select a preset. Preset management does not commit project drafts or change Undo.

## HDR and input requirements

Editing previews are SDR. PQ/HLG export reads original high-precision frames and uses software HEVC 10-bit; HDR H.264 and GPU HDR are rejected.

- PQ uses a 203-nit subtitle reference white by default. HLG uses 1000-nit, zero-black reference conditions.
- Complete static HDR10 mastering metadata is retained; MaxCLL/MaxFALL become unknown 0/0 after composition.
- Dynamic HDR such as Dolby Vision/HDR10+, changing/incomplete mastering, and unsupported color semantics are rejected.
- Input must have supported integer planar YUV, square pixels, and canvas dimensions matching visible video. VFR retains source presentation timestamps.
- Rotation, stereo, ICC, interlace, corrupt/dynamic formats, and negative timestamps unsupported by the muxing path report errors.

Copy audio retains packet content; AAC uses the selected bitrate. Container/timestamp incompatibilities report diagnostics rather than silently shifting the source timeline.

For ASS/SRT files, use [Subtitle editing](subtitle-editing.md). Package requirements and verification are in [Publishing](publishing.md).
