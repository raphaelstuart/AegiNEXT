# Borderless tabs, timeline display/themes, and DSL completion

[English](timeline-completion-polish.md) | [简体中文](../zh-CN/checkpoints/timeline-completion-polish.md)

Date: 2026-10-05. Continued from the existing worktree without staging/committing/reverting user work. Product version 0.1.0. This is a dated milestone record, not a claim about later changes or rebuilt packages.

## Phase 1: Dock borders

Checkpoint: Layout owns the frame; multiple tabs highlight text/background only.

- Default ToolTabStrip filler drew top lines even with one tab; selected item drew side/bottom borders, creating corner clipping/residual lines.
- Removed filler/item normal/selected/hover strokes. Selection uses accent text/14% accent background, preserving padding.
- Preview/Subtitles/Timeline root Borders no longer duplicate frame. Layout retains shared corners/background/stroke/clipping and Dock templates/commands/drag/lifetime.

## Phase 2: Timeline display and snapping

Checkpoint: Independent energy/waveform visibility and true snap indication.

- Two buttons route Timeline model state through StyledProperty drawing switches. Analysis/viewport/playhead remain; fixed panel keeps switches across floating/docking. Session-only, no new restart preference.
- Magnet icon; quantization returns exact Boundary, used for the same drawn vertical outline. Alt/invalid placement/clamping away/release/cancel clear it.
- Light/dark palette covers background/tracks/clips/text/curves/markers/playhead/snap; subtle animation-row background improves audio-overlay readability.

## Phase 3: Palette selection and light audio colors

Checkpoint: Direct builtin round trips, theme adaptation without rewriting personal values.

- ComboBox uses stable scheme objects/SelectedItem; language updates labels without rebuilding ItemsSource, eliminating stale-index writeback.
- Classic/Ice/Ember/Grayscale resolve actual dark/light palettes. Classic dark preserves the old table; light uses bright low energy/dark high energy. Settings preview/timeline share resolver.
- AdaptToTheme defaults true; old preferences identify original scheme. Manual/custom disables it and preserves values. Custom can directly return to Classic; independent persistence never modifies project.

## Phase 4: DSL completion

Checkpoint: Visible caret popup, native editing and diagnostics retained.

- Real TextInput schedules contextual completion; Ctrl/Cmd+Space opens explicitly. Current-window Overlay anchors to native TextLayout caret, reserves no bottom space/takes no focus.
- Arrows/Enter/Tab/Esc/pointer insertion remain. IME preedit/blur/read-only/detach cancel pending requests and popup.
- Removed persistent bottom hint, retained errors/location. Whitespace including Tab completes. Theme PreviewSurface/PreviewBorder verified in actual dark/light pixels.

## Verification

| Check | Result | Evidence |
|---|---|---|
| Palette/preferences/drafts, completion contexts, snap result domain regressions | 103/103 passed | artifacts/verification/timeline-polish-domain.log |
| Workbench/settings/narrow floating/timeline/DSL real input and pixels | 85/85 passed | artifacts/verification/timeline-polish-ui.log |
| Rider affected-file error analysis | No errors | Root and three specialist lint_files results |
| macOS Release build | Passed | Build/test logs above |
| Windows x64 self-contained verification build | Passed | artifacts/verification/timeline-polish-windows-build.log |

Tests cover actual arrow-key builtin→Classic selection, refresh/reopen/autosave/custom/theme cycles; audio real buttons/identity/floating/pixels/data/viewport; snap press→move→Alt→release/pixels; DSL character input/popup geometry/themes/Tab/Enter/Esc/blur/IME; Dock dark/light/240-DIP floating/real multiple-tab clicks/no single-tab residue.

First UI run passed 83/85. Fixtures incorrectly used unhandled ComboBox Home and a floating root without XAML NameScope. Corrected to actual arrows and fixed panel scope; production was unchanged for these fixture fixes. A build attempted before parallel edits completed failed and was not counted as test evidence. Original UI log: timeline-polish-ui-before.log.

Rendered light timeline/dark-light completion/narrow tabs were inspected under artifacts/verification/timeline-polish-ui. Tests close their windows. Headless input/pixels do not replace actual DPI/touchpad/desktop docking acceptance.

Windows artifacts/verification/preview-win-x64 is a self-contained verification build; no complete media release ZIP was rebuilt in this milestone. See [Quick Start](../quick-start.md), [workbench](../workbench.md), [DSL](../effect-dsl.md), [windowing](../workspace-windowing.md).
