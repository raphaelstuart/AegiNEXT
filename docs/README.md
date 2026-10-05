# AegiNext documentation

[English](README.md) | [简体中文](zh-CN/README.md)

User guides and developer documentation live in `docs/`. Checkpoints and acceptance evidence are recorded in `docs/checkpoints/`. English documents keep these paths; Simplified Chinese documents mirror them under `docs/zh-CN/`.

## User guides

- [Quick Start](quick-start.md): launch, first subtitle, save, and export.
- [Workbench guide](workbench.md): tracks, timeline, styles, keyframes, layouts, settings, and logs.
- [Subtitle detail editing and formats](subtitle-editing.md): dockable rich text, karaoke, advanced code, bounded playback, ASS and SRT.
- [Export and HDR](export.md): encoding, audio, restrictions, and color contracts.
- [Effect DSL and examples](effect-dsl.md): fixed/flexible timing, vectors, full RGBA, keyframes, and the settings editor.

## Developer guides

| Topic | Document |
|---|---|
| Module ownership and dependency direction | [Architecture](architecture.md) |
| SDK/native/RID requirements, locks, focused tests | [Building](building.md) |
| Self-contained packages, dependency closure, signatures, relocation | [Publishing](publishing.md) |
| Session ownership, MVVM, panels, shared controls | [Composable workspace](composable-workspace.md) |
| Dock space, presets, floating windows, persistence | [Layouts](layouts.md) |
| Native buttons, title bars, cross-window menus | [Windowing](workspace-windowing.md) |
| Media facts, FFprobe, process boundaries | [Media probing](media-probing.md) |
| Decode, seek, PTS, frame lifetime | [Video decoding](video-decoding.md) |
| Clock, audio, presentation scheduling | [Video playback](video-playback.md) |
| SDR derivation and display delivery | [Video preview](video-preview.md) |
| F16 composition, shaping, paths, rendering API | [Rendering](rendering.md) |
| Optional macOS native HDR diagnostic | [Native HDR](native-hdr.md) |
| Scrubbing performance and measurement limits | [Interactive preview performance](interactive-preview-performance.md) |

Invoke `$aeginext-effect-dsl` when authoring a script, or `$aeginext-controls` when developing or integrating a shared project control. Portable definitions are in [DSL skill](../.agents/skills/aeginext-effect-dsl/SKILL.md) and [controls skill](../.agents/skills/aeginext-controls/SKILL.md).

## Implementation evidence

- [Subtitle formats and the dockable shared editor](checkpoints/subtitle-format-and-details.md)

- [Preview decoder modes, missing color tags and platform verification](checkpoints/video-decode-modes-and-missing-color.md)

- [Preview quality, track style policy, bilingual docs, and skills](checkpoints/preview-quality-and-track-style-policy.md)

- [GPU encoding, project titles, default colors](checkpoints/export-title-colors.md)
- [Borderless tabs, timeline themes/display, DSL completion](checkpoints/timeline-completion-polish.md)
- [Track styles, automatic positioning, vectors, highlighting](checkpoints/track-styles-and-karaoke.md)

Each record states its own test and platform scope. A planned feature is not evidence of a current UI entry point; source tests are not evidence that an older release package contains the change.
