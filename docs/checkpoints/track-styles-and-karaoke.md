# Track styles, automatic positioning, vectors, and character highlighting

[English](track-styles-and-karaoke.md) | [简体中文](../zh-CN/checkpoints/track-styles-and-karaoke.md)

Date: 2026-10-05. Product 0.1.0/project v3. Work continued from the existing tree with serial builds/tests. This records the earlier track-style milestone; later removal of bulk styling and optional automatic application are described in [current workbench behavior](../workbench.md).

## Phase 1: Commit earlier production work

Checkpoint: Explicitly exclude tests/docs and the new feature work.

- Commit 2fc544303371a4d29b63117b7e77e5eb0e17601f: feat: add hardware export and refine project title and colors.
- 29 production files, audited to exclude Tests/docs/native tests.
- Existing test/document changes remained. At this milestone end, later features were uncommitted and the index empty; a subsequent request committed that production baseline separately.

## Phase 2: Track styles

Checkpoint at this milestone: Current/all-track application, future inheritance, Undo/storage.

- Added current/all-track context entries using stable Track/Preset IDs, including empty tracks. **The all-track entry was removed by the subsequent optimization; it is not a current user operation.**
- DefaultStyle saved an in-project snapshot plus source ID/name; personal preset modifications/deletion did not rewrite it.
- Existing styles/defaults/fonts committed in one transaction, preserving time/effect layers/keyframes/character segments.
- Manual/actual shortcut/F8/import inherited defaults; split/move retained clip style.
- PrepareAsync handled zero-subtitle projects. Layout owned space, Timeline local menu interaction, Workspace orchestration.

## Phase 3: Position and vectors

Checkpoint: Exactly zero centered X, shared measurement/rendering, independent draft restore.

- Horizontal auto layout uses actual ink rather than advance width. Center is zero, left/right signed margins.
- Automatic→explicit preserves pixels; restore removes positional edits with one Undo.
- Anchor/Pivot/Offset use three VectorDraftInput rows, normalized step 0.1/pixel step 1.
- Stable component field lookup is local, no external XAML scope; Esc restores current component only.

## Phase 4: Highlight style

Checkpoint: Preset appearance preserves geometry/time and main/worker consistency.

- Highlight Style selector includes default. Existing highlight action applies; selection/language/refresh does not mutate project.
- Scope is fill/stroke/width/shadow, preserving font/size/alignment/position.
- One KaraokeHighlightStyle per subtitle, linear HDR-capable; existing segment times/grapheme ranges retained.
- Same shaping; base rendering excludes highlight region before highlight appearance, avoiding leftover large outline/shadow.
- Split inherits highlighted side. Merge compares actual appearance excluding source ID/name; mismatches reject, one-sided highlight uses that side.
- Clear removes segments/snapshot; missing optional v3 field retains legacy behavior, unknown/duplicate rejects.

## Phase 5: Platform tests and documentation

Checkpoint: Focused tests, independent worker output, static/Git checks.

| Scope | macOS | Windows x64 / Parallels ARM64 |
|---|---:|---:|
| Core position/highlight/HDR/invalid model | 16 passed | 16 passed |
| Application track/preset/position/transactions/highlight storage/Undo | 56 passed | 56 passed |
| Rendering actual ink/multiline highlight/fill/stroke/shadow pixels | 13 passed | 13 passed |
| Desktop position drafts/measurement | 7 passed | 7 passed |
| Headless track menu/keys/vectors/highlight/settings/global input | 38 passed | 38 passed |
| Media actual wire/independent worker highlighting | 3 passed | 3 passed |

133 distinct focused cases per platform, no skips. UI tests close main/settings/floating/isolated hosts.

Real output sends track defaults/nonempty highlight snapshots to the newly built worker, generates two H.264 files, reads first/next frames to confirm base then saved red highlight instead of legacy white, and checks PTS/temporary cleanup. This uses source Release main/worker, **not newly repackaged release ZIPs**.

Fixture corrections: standalone windows lacked parent XAML scope; ImmutableArray round trips must compare contents; worker ProjectStore—not message JSON options—decodes MediaTime. macOS Application first 55 passed/1 failed then fixed/retried; Media first 2 passed/1 wire assertion fixed/retried.

Windows exposed input routing treating focused closed top menus as open input contexts, blocking F8. Policy now checks actual open state while retaining PopupRoot/open menu/dropdown/text/modal exceptions. Added focused-closed/open cases; final 38 UI passed. Original failure retains focus/open/position/error diagnostics.

Rider error analysis found no new errors; builds had no warnings/errors; git diff --check passed. Results: artifacts/verification/track-styles-*.trx, Windows track-styles-windows-*.log; final UI track-styles-ui-final.trx / track-styles-windows-ui-final.trx.

Narrow Dock/cross-DPI/real drag/mixed-font appearance still need manual acceptance; automation checks actual control input/geometry/pixels/process output. See [Quick Start](../quick-start.md), [workbench](../workbench.md), [architecture](../architecture.md).
