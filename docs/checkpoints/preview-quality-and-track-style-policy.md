# Preview quality, safe track styling, and bilingual documentation

[English](preview-quality-and-track-style-policy.md) | [简体中文](../zh-CN/checkpoints/preview-quality-and-track-style-policy.md)

Date: 2026-10-05. Product version remains **0.1.0**; project format remains **v3**. The preceding production work was committed first as `ea4a5ce` (`feat: add track style presets and karaoke appearance`), with 29 source files and no tests/documentation. The changes described here remain in the working tree. Unrelated native tests were preserved.

## Phase 1 — Commit boundary and interfaces

Checkpoint: verify the index, commit only the preceding production source, and separate personal preview preferences from project track styling. Completed: the commit contains only `src/` production files; the index was empty before the new work.

## Phase 2 — Preview quality

Checkpoint: quality changes must affect real conversion/composition, persist personally, and preserve playback/selection.

- Stable `LOW`, `STANDARD`, and `HIGH` values cap output at 960×540, 1280×720, and 1920×1080. Default is LOW; previous preference files without the new field also load LOW without a load-time rewrite.
- The selector appears beside the SDR badge and works before loading media. Translated labels retain the stable selection. Smaller video sources are not upscaled.
- Background conversion and scene composition both respect the chosen budget. Interactive preview caps at 540p; final positioning restores the selected quality. Export retains its existing quality path.
- Quality revisions invalidate cached/in-flight results. Playing changes do not pause/seek; paused changes refresh the current position. Native converter instances are reused by quality and disposed once with their owner.
- Real FFV1 fixtures exercise decode, native SDR conversion, composition, dimensions, source-frame preservation, interactive restoration, and small-source behavior. Fixtures include complete BT.709/range/chroma metadata.

## Phase 3 — Track styling

Checkpoint: no implicit global restyling; existing clips change only by an explicit track-local decision.

- A style-name badge appears below the track name, sharing header geometry with drawing and hit tests. A disabled auto-style badge is dimmed; clicking it still selects the track.
- The track context menu toggles automatic track styling. Toggling preserves stored defaults and existing clips. Disabled tracks create subtitles using the Styles panel's selected preset, or base style when none is selected.
- Choosing a track preset with existing clips opens an awaitable Yes/No/Cancel dialog. No is the default: update only the future-creation default. Yes also updates this track's existing clips; Cancel/Esc/window close changes nothing. Empty tracks need no confirmation.
- The application API defaults to `updateExisting: false`. All-track restyling APIs, commands, labels, and menu entries were removed.
- Creation, timing, and selected-track import use one resource-prepared transaction. They capture track/preset/time before awaiting font preparation. Timing exit received during preparation retains its end time until creation succeeds.
- Project v3 adds optional `autoApplyStyle`, defaulting true for preceding v3 data. Undo, layer/effect identity, existing clip style on split/cross-track move, and collision validation remain intact.

## Phase 4 — Documentation and skills

Checkpoint: complete English/Chinese counterparts, working language/resource links, and usable project-specific skills.

English documentation remains at existing `docs/` paths; Chinese counterparts live under `docs/zh-CN/`. Root, layout, and font-fixture READMEs also have `README.zh-CN.md` counterparts. Historical six-panel/project-v2 wording is identified as historical; current guides describe seven panels/project v3. Broken references to absent `plans/*.md` were replaced with current documents.

Repository skills live at `.agents/skills/aeginext-effect-dsl/` and `.agents/skills/aeginext-controls/`; local discovery links point to those same sources. Each has a task-specific SKILL, UI metadata, and focused references. Both pass the skill-creator validator. All 50 documentation/README files (22 documentation pairs and three other README pairs) passed local-file, heading-anchor, language-switch, and same-language-link checks: 311 local links, no failures (`bilingual-docs-links.json`). Independent trial authoring produced a different color/vector script; the real parser/compiler checked 12 sources at seven durations each, including 1µs, short clips, exact boundaries, and nonzero content offset: **84 checks passed**.

## Phase 5 — Verification

| Scope | macOS | Windows x64 |
|---|---:|---:|
| Core DSL / tracks | 57 passed | 57 passed |
| Application track transactions / presets | 34 passed | 34 passed |
| Desktop preferences / scheduling / real conversion / workflows | 41 passed | 41 passed |
| Headless UI selection / confirmation / shortcuts / quality | 42 passed | 42 passed |
| Real independent worker / wire / subtitle output | 3 passed | 3 passed |

macOS: **177 unique tests passed, no skips**. The initial Desktop run passed 38 and exposed three fixture failures; corrected video metadata and the existing licensed Noto font then passed all three targeted retries. Test-only xUnit-v2 API/analyzer errors were corrected before execution. Production Release build completed with zero warnings/errors; affected production and test files passed Rider error analysis. Tests close their windows and dispose their sessions.

Evidence is in ignored `artifacts/verification/`: `styles-quality-core-macos.trx`, `track-style-options-application-macos.trx`, `preview-quality-desktop-macos.trx` plus `preview-quality-desktop-macos-retry.trx`, `styles-quality-ui-macos.trx`, `styles-quality-media-macos.trx`, and `skill-forward-check-result.json`. Windows x64 in Parallels passed the same 177 tests with no skips on the final sources; reports are `styles-quality-windows-*.trx`. The merged latest-result audit is `styles-quality-test-summary.json`.

Real touchpad behavior, cross-DPI/narrow-dock visual review, and perceived playback smoothness remain manual acceptance items. Pixel-budget tests do not establish an FPS improvement. This iteration verifies source builds and real worker output; it does not replace the previous self-contained development release packages.
