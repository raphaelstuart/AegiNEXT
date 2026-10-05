# Preview decoder and missing color tags checkpoint

[English](video-decode-modes-and-missing-color.md) | [简体中文](../zh-CN/checkpoints/video-decode-modes-and-missing-color.md)

## Completed behavior

The repair covers SDR H.264 files with absent range, matrix, primaries and transfer tags, including the reported High-profile 1280×720, 60000/1001 fps, AAC LC stereo 44100 Hz input class. Native color resolution derives effective SDR parameters without replacing the raw facts. Preview and export use the same policy; explicit unsupported or conflicting HDR/film-grain metadata remains an error.

Settings → Media → Preview decoder offers Auto, CPU and GPU, persists the choice, translates immediately, and displays the actual decoder and fallback reason. With an open video, switching waits for a converted frame before accepting the choice. Failure restores the old decoder, position and playback state. Epoch checks prevent stale frames or a rollback from replacing a newly opened file. Personal preferences do not edit the subtitle project or its undo history.

Preview and export statically link `native/shared` and own independent sessions. Auto may restart in software before the first delivered frame after a confirmed hardware failure. It does not retry corrupt input, I/O, cancellation or allocation failures, or switch backends after delivery. Export decode mode is independent of preview preferences and encoder mode. Export ABI 3 reports actual decoding and effective output color; mux validation uses these effective tags.

Hardware v1 supports opaque 4:2:0 H.264 8-bit and HEVC 8/10-bit through VideoToolbox and D3D11VA. Download retains NV12/P010 precision and actual frame properties. Software and hardware may expose different coded height/crop layouts, so equivalence checks compare visible samples, timing, bit depth and color/HDR metadata. No unavailable coded padding is fabricated.

Real regressions also fixed a borrowed stream pointer after Auto restart and a missing last-frame duration when muxing the NUT intermediate to MP4. Export tests compare visible pixels, fixed color patches, all frame PTS, and copied AAC packet hashes, rather than checking only that an output file exists.

## Automated evidence

Executed evidence is saved under `artifacts/verification/`, with media-core fault-injection logs under `artifacts/media-core-review/`. Build and test scopes are recorded separately; counts from overlapping focused runs must not be added as unique coverage.

| Focused scope | macOS ARM64 | Windows x64 on ARM64 VM |
|---|---:|---:|
| Debug media decoding/preview | 119/119 | 123/123, including selected export cases |
| Preview controller, preferences and localization | 59/59 | 59/59 |
| Settings UI | 13/13 | 13/13 |
| Release ABI and decoder modes | 16/16 | 16/16 |
| Native decoder/export, each Debug and Release | 4/4 + 2/2 | 4/4 + 2/2 |

All listed test runs report zero skips. The Windows session test cannot enter its successful-hardware post-delivery branch; that branch is executed on macOS. Separate export ABI/wire/worker/color runs pass 60/60, and the final union of all exporter cases with decoder/navigation lifetime checks passes 40/40 on macOS. Both platforms build Workbench Debug and Release successfully.

- macOS ARM64 Debug and Release compile the shared decoder/export cores. Native tests cover ABI, planes, color dependencies and controlled decoder failures. The hardware post-delivery refusal case runs against a real VideoToolbox session; absence of a fixture is explicitly a CTest skip.
- Real software/VideoToolbox cases cover 720p and cropped 1080p H.264/HEVC 10-bit, visible sample equality, EOF, seek, mastering/content-light side data and native lifetime counts. Untagged preview is checked against a bitstream with identical compressed pixels and explicit BT.709 tags.
- Export coverage includes software/actual hardware decoding, transparent and semi-transparent overlays, effective tag validation, unchanged unknown raw tags, frame count/PTS/duration, AAC packet copy, compatible midstream tags and atomic failure on real color changes.
- Controller, preferences and actual Avalonia headless UI cases cover switching while playing, failed first-frame presentation, rollback/open races, language changes, persistent choices, busy state, actual backend text and settings-window cleanup.
- Windows 11 runs the x64 native outputs and worker under ARM64 emulation, using FFmpeg 9.0.2 and the Parallels WDDM driver. The device exposes no usable video-decoder GUID: Auto restarts in software and required GPU returns a hardware negotiation error. This exercises Windows software decoding/encoding and fallback, but does not establish successful decoding on a physical D3D11VA GPU.
- Managed builds report zero warnings/errors. Rider's only error-level result is the existing CA1822 on `VideoExporter.ExportAsync`; its service-instance suppression is respected by the build. macOS native compilation uses `-Werror`; linking still warns that the local Homebrew FFmpeg requires macOS 27 while the project targets 14.

## Performance evidence

The same 4K VFR long-GOP source and four animated subtitle tracks are measured in five rounds per mode. Interactive output is 960×540. The first target in each round is excluded from warm percentiles; 155 warm samples cover alternating seeks between 4 and 7 seconds. Measurement includes real decoding, SDR conversion and CPU scene composition, and excludes UI upload.

| Alternating seeks | Total P50 | Total P95 | Codec P95 | Download P95 |
|---|---:|---:|---:|---:|
| Previous software baseline | 291.5 ms | 370.9 ms | Not instrumented | 0 ms |
| Software | 280.2 ms | 353.5 ms | 308.0 ms | 0 ms |
| Auto, confirmed VideoToolbox | 468.3 ms | 573.7 ms | 469.9 ms | 52.8 ms |
| Required GPU, confirmed VideoToolbox | 459.1 ms | 571.1 ms | 466.9 ms | 54.6 ms |

Software total P95 changes by −4.69%, within the accepted 10% regression bound. Hardware is slower for this repeated long-GOP preroll workload; these measurements do not support promising faster scrubbing. Codec/download are API-stage counters, and asynchronous GPU work may finish during download.

The scrub comparison uses Debug throughout. A separate final-core Release run measures five rounds of consecutive targets between 4 and 5 seconds: total P50/P95 is 39.2/41.6 ms for CPU, 41.8/45.1 ms for Auto, and 42.2/45.4 ms for required GPU. Auto/GPU both confirm VideoToolbox in every round. Initialization P95 is approximately 11 ms in all three modes. Configuration and navigation differ from the scrub comparison, so these runs are not combined into a single regression percentage. Stage timings and session evidence are retained in `video-decode-performance-summary.json` and the six per-mode JSON reports.

## Acceptance boundaries

The original video file was not provided; the attachment supplies its displayed properties. Automated fixtures reproduce that input class. Original-file interaction, physical Windows GPU decoding, Linux, minimum macOS deployment and native HDR display remain separate acceptance items. No release package or project-format change is included. The existing running workbench is not closed or restarted automatically.
