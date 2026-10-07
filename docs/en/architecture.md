# Architecture

[English](architecture.md) · [简体中文](../zh-cn/architecture.md) · [All guides](README.md)

## Find the owner

| Location | Responsibility |
|---|---|
| `src/AegiNext.Core` | Rational time, immutable project/scene models, validation, animation, masks, and effect DSL |
| `src/AegiNext.Application` | Editing transactions, Undo/Redo, storage/resources, and subtitle format exchange |
| `src/AegiNext.Rendering` | UI-independent Skia/HarfBuzz shaping, scene geometry, and linear F16 composition |
| `src/AegiNext.Media` | Probing, frame ownership, native adapters, playback/analysis, preview, and export orchestration |
| `src/AegiNext.Desktop` | Avalonia workspace, panels, controls, menus, preferences, and platform hosts |
| `src/AegiNext.ExportWorker` | Independent `aegn-exporter` process sharing Core and Rendering |
| `native/decoder`, `native/audio`, `native/export` | Separate FFmpeg/SDL3 C ABIs |
| `native/` | Optional macOS HDR diagnostic backend |
| `Tests/`, `scripts/` | Focused verification, build, and packaging tools |

Core has no Avalonia, Dock, FFmpeg, Skia, or filesystem dependency. Application owns business edits; Desktop composes services. Rendering and Media use explicit contracts and resources; business view models do not hold controls, Dock objects, or bitmaps.

## Time and storage

Time is rational `MediaTime`; Clip visibility is `[Start, End)`. Decoding and VFR use actual presentation timestamps. Subtitle-format exchange maps confirmed playback origin only at the boundary; relative animation/karaoke time is retained.

New `.aeginext` files use **v6**. v3–v5 migrate without moving subtitle/layer times; an unknown playback origin remains unknown until confirmed. Missing/null legacy masks are accepted, but unsupported nonempty legacy local masks reject loading with the affected object identified. v1/v2 are unsupported.

Loading validates fields, references, overlaps, geometry, and budgets. Saving uses same-directory temporary files and atomic replacement. Videos are referenced, not copied; managed resources use confined relative paths and optional hashes. Save As rebases resources/references; a cross-directory relocation clears history that could restore obsolete paths.

Autosave captures committed content and view state through a serialized persistence coordinator. It does not commit drafts, steal focus, or change Undo. Saving a captured snapshot keeps newer edits dirty.

## Editing and presentation

`ProjectEditor` commits immutable snapshots. Multi-Clip edits/imports are atomic; failed validation leaves the project and history unchanged. Layout changes retain one session and fixed panel instances. Personal layouts, styles, scripts, preferences, and logs are separate from project content.

Preview uses the same scene geometry as editing/export and presents SDR derivatives. The renderer retains linear F16 until display/encoding; worker export reads original media frames. Every asynchronous result checks time, revision, request identity, and ownership before delivery.

## Contribute

Use Allman braces, file-scoped namespaces, one top-level type per file, `var`, and target-typed `new()` where suitable. Nullable checking, analyzers, and warnings-as-errors are enabled. Public/protected API docs describe the contract.

Put focused tests in the owning Tests project. Change a native contract together with managed bindings, worker protocol, capability/version checks, and layout tests. Keep commits scoped with short English `feat:`, `fix:`, or `chore:` titles. Exclude generated artifacts and run `git diff --check` plus affected tests.

Continue with [Workspace integration](composable-workspace.md), [Media](media.md), [Rendering](rendering.md), or [Building](building.md).
