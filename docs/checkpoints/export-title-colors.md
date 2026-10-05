# GPU encoding, project titles, and default colors

[English](export-title-colors.md) | [简体中文](../zh-CN/checkpoints/export-title-colors.md)

Date: 2026-10-05. Continued from uncommitted work, preserving user index/unrelated modifications without staging/committing in this milestone. Version 0.1.0/project v3.

## Phase 1: Default colors

Checkpoint: Immediate restoration/persistence, other preferences/project retained.

- Added Restore Default Colors from WorkbenchPreferences defaults, restoring accent and full audio scheme with one event through Workspace persistence.
- Cleared these invalid/incomplete drafts; refill causes no second write. Theme/language/menu/shortcuts/volume stay.
- View wires button/bindings; model owns drafts; project/layout do not participate.

## Phase 2: Title projection

Checkpoint: One name source, successful Save As refresh, failure/cancel retain name.

- WorkbenchProjectTitle chooses saved basename, explicit project name, media basename, localized Untitled.
- Main AegiNEXT - Project name, dirty adds ` •`; floating adds panel. Registry synchronizes native/custom title.
- Save/export suggestions share session.ProjectDisplayName; no document-name/format/Undo change.
- Tests open/new/Unicode/cancel/failure/Save As/Undo/Redo/language/floating synchronization.

## Phase 3: Actual GPU video encoding

Checkpoint: Immutable request→worker→native hardware initialization, no silent CPU fallback.

- CPU libx264/libx265 CRF/speed default; GPU 0.1–200 Mbps/default 8. Separate drafts, validate active mode only.
- VideoToolbox allow_sw=0/require_sw=0; Windows NVENC→QSV→AMF checks format and actually initializes with aggregated failure details.
- Speed maps VideoToolbox prio_speed, NVENC p3/p4/p5, QSV fast/medium/slow, AMF speed/balanced/quality. No equal quality/speed promise across backends.
- SDR H.264 NV12 / HEVC P010 10-bit; HDR remains verified software HEVC, hardware rejects.
- Export ABI 2 uses a 72-byte request carrying mode/bitrate; managed/native/worker/tests upgraded together.
- Actual encoder identity propagates progress/result/log and is verified again before commit; stale workers reject.
- Decode/linear composition unchanged; option accelerates encoding only.

## Phase 4: Tests and development packages

Checkpoint: Automation, native run, visual/physical hardware evidence separated.

Serial builds/tests; UI hosts cleaned. Logs under artifacts/verification/export-title-colors-:

| Check | Result | Evidence suffix |
|---|---|---|
| Colors/name/save/new/open/Undo/registry domain | 17/17 passed, no skips | domain.log |
| Real settings input/reset/Unicode/title/GPU/layout/export lifecycle | 10/10 passed, no skips | ui-final.log |
| macOS packaged worker/ABI/wire/identity/CPU HDR/audio/cancel/actual GPU H.264+HEVC | 33/33 passed, no skips | media-macos-final.log |
| Windows packaged worker/GPU unavailable→CPU/HDR/audio/cancel/identity | 33/33 passed, no skips | media-windows.log |
| macOS Export native color/ABI CTest | 2/2 passed | native-macos-build.log / native-macos-tests.log |
| Windows Export native color/ABI CTest | 2/2 passed | native-windows-build.log / native-windows-tests.log |
| Actual macOS native GPU | 10 frames, h264_videotoolbox | native-gpu-macos.log |
| Actual Windows VM native | NVENC/QSV/AMF failed explicitly, then libx264 completed 10 frames | native-windows-probe.json / native-gpu-windows.log / native-cpu-windows.log |
| Self-contained packages | 0.1.0 main/worker/tools/native complete | publish-macos.log / publish-windows.log |
| Package probes without development PATH/media env | Both: audio device playback, 271-frame output, color readback, cancel cleanup | package-macos.json / package-windows.json |
| File hashes | macOS 324 / Windows 313 matched, no extra | package-hashes.json |
| ZIP integrity | unzip -tq passed both | Terminal execution |
| macOS signature | codesign --verify --deep --strict passed | Terminal execution |

GPU outputs verify backend/H.264 8-bit/HEVC 10-bit/BT.709/AAC plus first PTS/overlay luminance. CPU HDR retains PQ/HLG pixels/metadata. Final 33 cases use actual package worker/tools with explicit configuration, not skips counted as pass.

Rider found no new errors. Existing VideoExporter.ExportAsync instance-could-be-static diagnostic remains with baseline CA1822 suppression, preserving service boundary; managed builds no warning/error. git diff --check passed.

Windows package moved from local C: to shared X: before tests/probe, process X64 without system dotnet/developer PATH. First relative output resolved under Windows C: cwd; package was moved to final path and script changed to absolute output.

## Artifacts and acceptance limits

- macOS: artifacts/releases/export-title-colors-osx-arm64/AegiNext.app; ZIP AegiNext-0.1.0-osx-arm64-export-title-colors.zip.
- Windows: artifacts/releases/export-title-colors-win-x64/AegiNext/aegi-next.exe; ZIP AegiNext-0.1.0-win-x64-export-title-colors.zip.
- Both include self-contained .NET/worker/tools/audio/export/native closure/licenses/manifest, preserving previous packages.
- Actual FFmpeg closure requires macOS 27.0; plist/manifest record it, not macOS 14.
- Parallels exposes no NVIDIA/Intel/AMD encoder; explicit failure and CPU recovery are accepted. Physical GPU success/driver variation/DPI visuals remain device/manual work.
- UI automation exercises controls/window state; complex desktop appearance needs user acceptance. Installers/notarization/Windows signing excluded.

See [Quick Start](../quick-start.md), [workbench](../workbench.md), [export](../export.md). Later source changes are not implicitly included in these dated packages.
