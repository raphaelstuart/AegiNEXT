# Subtitle format exchange and the shared detail editor

[English](subtitle-format-and-details.md) | [简体中文](../zh-CN/checkpoints/subtitle-format-and-details.md)

Date: 2026-10-05. This checkpoint records implementation and automated verification of the content model, transactions, persistence, shared rendering, format exchange, detail editing, and bounded playback. The child editor uses the existing Dock workspace: it can float, dock, hide, reopen, and retain its view identity. Layout v3 reads v1/v2 layouts and adds the initially hidden detail panel.

The Format menu owns recursive Aegisub and SRT import/export groups. Existing command values and configured SRT shortcuts retain their meaning. Subtitle rows show readable text and a derived type. Rich text, karaoke, and advanced code share content and draft ownership; clip properties are available in the rich view too. Programmatic advanced-source updates are distinguished from queued TextChanged input, preserving precise karaoke times.

`RichSubtitleEditor` uses native Avalonia text/IME input plus shared rendering geometry and a cropped bitmap. Complete grapheme boundaries govern selection, deletion, preedit underlines, and the native caret. Karaoke gestures freeze their time projection, release pointer capture on cancellation, and commit once on release. Global draft preparation is atomic; invalid code or timing blocks target/view/close/save until corrected or explicitly restored. Source-projection compatibility failures disable only the advanced field. Bounded playback uses the shared controller with ownership/revision checks, including a second check after asynchronous range cleanup.

## Executed content, format, rendering, and playback verification

The unified model retains plain text and grapheme-safe UTF-16 inline ranges. Karaoke clips have stable identities and exact rational times. Text editing redistributes timing within affected regions; split, merge, whole-line style, and selection style operations are undoable. Whole-line styling clears inline overrides and retains karaoke. Project v4 upgrades v3 in memory; style library v3 reads v1/v2 without rewriting files on load.

Preview, detail editing, and the real export worker share mixed-font/style layout, baselines, grapheme hit geometry, decorations, and highlighting. System font fallback operates on complete graphemes and merges adjacent equal-face runs for shaping. Chinese, Latin, and ZWJ emoji were exercised on macOS. Explicit project fonts still diagnose missing glyphs. Cache clearing and disposal release all shaping and font resources.

All final focused sets passed with zero skipped:

| Set | Passed | Coverage |
|---|---:|---|
| Core | 80 | Content type, grapheme ranges, validation, style and animation evaluation |
| Application | 187 | Old-file migration, local timing redistribution, Undo/Redo, split/merge, ASS/SRT, advanced source mapping, and atomic import |
| Rendering | 113 | Mixed fonts/styles, three karaoke highlights, hit geometry, and resource release |
| Media sessions | 25 | Bounded audio/video ends, exact times, and device draining after pause |
| Desktop Controller | 24 | Loop ownership, main seek, cancellation during start, failure cleanup, and stale requests |
| Real worker and transport | 6 | Project fonts, underline/strike, karaoke snapshots, Chinese/ZWJ fallback, and actual H264 exports |

Final Core, Application, and worker results are saved in [Core TRX](../../artifacts/verification/subtitle-core.trx), [Application TRX](../../artifacts/verification/subtitle-application.trx), and [worker TRX](../../artifacts/verification/subtitle-worker.trx). Earlier phase reruns are excluded from these counts.

The final Desktop build also built the worker with zero compiler warnings/errors. macOS arm64 decoding, audio, export, and native rendering libraries were built from installed dependencies and used by real worker tests. Some installed native libraries target macOS 27 while the project deployment target is macOS 14, producing native linker deployment warnings; this run does not verify older macOS versions. No .NET/Avalonia upgrades or commercial rich-text dependencies were introduced.

## Executed Desktop verification

Desktop focused tests: **64 passed, zero skipped**. Run the Desktop project with this filter:

```text
FullyQualifiedName~SubtitleDetails|FullyQualifiedName~SubtitlePreedit|FullyQualifiedName~SubtitleFormat|FullyQualifiedName~WorkbenchLogShortcutMigration|FullyQualifiedName~WorkspaceLayoutMigration|FullyQualifiedName~WorkspaceLayoutValidator|FullyQualifiedName~WorkbenchPreferences|FullyQualifiedName~SubtitleRow|FullyQualifiedName~WorkspaceDraft
```

The count is 63 in the first final aggregate plus the added derived-type case, executed separately. It includes transaction/Undo, precise time retention, strict UTF-8/BOM handling, independent overlap tracks, conversion cancellation, old shortcut/layout migrations, and draft conflict checks. **Controller playback tests are excluded from this count.**

Avalonia Headless UI: **36 passed, zero skipped**, with this filter:

```text
FullyQualifiedName~SubtitleDetailsEditing|FullyQualifiedName~WorkbenchWindowRegistryUi|FullyQualifiedName~WorkbenchLayoutsUi|FullyQualifiedName~NumericDraftEditing|FullyQualifiedName~LocalizationUi
```

These tests exercise actual KeyTextInput for Chinese plus a ZWJ emoji, the native IME client contract with a cursor inside an emoji, toolbar selection retention, rich-view clip editing, invalid-code target/tab/floating-close guards, drag-release Undo, Escape releasing pointer capture, Float→Dock persistence and view reuse, localization, and menu projections. Test hosts close on completion and explicitly discard invalid test drafts during cleanup.

Both projects compiled without compiler warnings/errors. Rider checks of the new editor, coordinator, projection, format workflow, dialog, recursive native menu, and layout migration showed no normal WARNING/ERROR. Existing naming/default-argument/nullability style warnings in older controls/hosts remain outside this change. Language packs have identical 525-key sets; `git diff --check` passed.

## Native and manual acceptance

The existing `--workspace-probe-auto` checked a real floating detail child, Chinese/ZWJ input rendering, all three tabs preserving content, docking with the same view, docked layout persistence, and closing/reopening the view. **All six new native detail checks passed**, as did 355 checks in the final focus-diagnostic run. Settings, export, and GetBinding exception counts were zero. Its isolated preferences live under the temporary `AegiNext.Workspace.Probe/<guid>` directory and are removed on completion.

The [native focus report](../../artifacts/verification/subtitle-details-native-focus.json) retained three failed menu-idle samples. Diagnostic evidence recorded `ApplicationIsActive=false` and the frontmost application `com.netease.uuremote` / UU远程, PID 7748, while the probe PID was 12395. The failures therefore occurred after another application took focus. Assertions were not relaxed, and the remote-control application was not modified. The complete menu-idle acceptance remains pending an undisturbed run.

Headless input and programmatic native probes do not replace manual macOS IME candidate-window, pointer docking, typography, and audible-loop acceptance. Windows/Linux interaction and cross-display behavior remain separately unverified for this milestone. See the [user guide](../subtitle-editing.md) for editing and supported ASS boundaries.


## 2026-10-06 detail interaction revision

Properties now wrap as complete field groups with muted titles above their inputs and X/Y labels beside vector inputs. A separate scrolling property area retains text-editing space. Numeric fields hide spinners. Body-selection style and karaoke highlight own separate drafts; highlights contain fill, outline, and shadow only. Applying a highlight preset does not import fonts or alter typography. Karaoke settings move from Styles to Details, with one Enable karaoke toggle replacing create, clear, split, and merge buttons. Disabling retains highlight configuration.

Editable clips represent complete graphemes. Import and advanced text edits retain affected-region duration, styles, and precise times. Positive durations beyond the cue end remain committable; the axis shows a selectable, draggable red overflow region without extending cue boundaries. Snap defaults on, targets a 10 ms grid, original boundaries frozen at gesture start, and the cue end, and can be temporarily disabled with Alt. The toggle creates no project transaction. Advanced code omits project-position tags and diagnoses entered `\pos`; ASS file position exchange remains supported.

The header has one segment play/pause button and a separate Loop toggle, initially off. Playback uses the selected clip or otherwise the whole cue. Loop updates during playback; changing it while paused never starts audio. Losing focus still pauses. The button returns to Play at a natural range end, and language changes never start playback. The restore action is labeled Reset.

The focused Desktop draft/color/position set passed **19/19 with zero skipped**; see [draft TRX](../../artifacts/verification/details-revision-draft.trx). Filter:

```text
FullyQualifiedName~SubtitleKaraokeStyleDraft|FullyQualifiedName~ColorDraft|FullyQualifiedName~ColorInputMode|FullyQualifiedName~SubtitlePositionDraft|FullyQualifiedName~SubtitlePositionEditMode
```

The final Headless UI set passed **79/79 with zero skipped**; see [UI TRX](../../artifacts/verification/details-revision-ui.trx). It includes actual snapping/overflow pointers, natural-range completion restoring the button label, paused Loop changes, separate body/highlight drafts, no highlight font-resource import, Chinese/ZWJ/IME, float/redock, narrow field groups, disabled colors in both themes, and existing Styles/font/window/localization behavior. Filter:

```text
FullyQualifiedName~SubtitleDetails|FullyQualifiedName~KaraokeStylePreset|FullyQualifiedName~ColorInputDisabledGeometry|FullyQualifiedName~KaraokeAxisOverflow|FullyQualifiedName~KaraokeAxisSnapping|FullyQualifiedName~NumericDraftEditing|FullyQualifiedName~LocalizationUi|FullyQualifiedName~StylesPanelLayout|FullyQualifiedName~ColorDraftInput|FullyQualifiedName~ColorInputBar|FullyQualifiedName~SubtitleFontEditing|FullyQualifiedName~SubtitleInputTypography|FullyQualifiedName~WorkbenchWindowRegistryUi|FullyQualifiedName~WorkbenchLayoutsUi
```

These sets do not sum prior repeated runs. Desktop/worker/UI compilation has zero warnings/errors. Rider reports no ordinary WARNING/ERROR in the changed views, draft, or tests. Both language packs contain 539 matching keys; `git diff --check` passes. Twelve Chinese captures live at `artifacts/verification/details-revision-{light,dark}-{950,460}-{fields,disabled,highlight}.png`, showing readable field/color geometry and retained text space. Captures do not replace user acceptance of the native application, IME candidate UI, or audible playback.


Capture review moved Enable karaoke into the header after Play/Loop, making it visible initially while allowing the narrow header to wrap. Only the related **9/9** were repeated for this final layout change; see [final-layout TRX](../../artifacts/verification/details-revision-ui-final-layout.trx), filtered by `FullyQualifiedName~SubtitleDetailsFieldsUiTests|FullyQualifiedName~KaraokeStylePresetUiTests`. These cases intersect the 79-case set and are not added to produce 88.

Other revised-layer verification: full Application **243/243**, and the new Rendering cue-overflow clipping case **1/1**. Grid/end/original-neighbor snapping first produced three real failing tests, then snap plus overflow pointers passed **7/7**; both [red TRX](../../artifacts/verification/karaoke-snap-red.trx) and [green TRX](../../artifacts/verification/karaoke-snap-green.trx) are retained.

Root's Desktop range/coordinator set initially passed 64 of 65 with one native-gated case unexecuted. Enabling native validation passed the same case **1/1**, so all **65 cases were executed successfully**. The [Desktop TRX](../../artifacts/verification/details-revision-desktop.trx) and [native preview rerun](../../artifacts/verification/details-revision-native-preview.trx) remain separate; this is not a 66th case. Loop toggling, natural-end notification, and cancellation were covered. Real worker/wire validation passed **4/4 with zero skipped**, including five actual video exports; see [worker TRX](../../artifacts/verification/details-revision-worker.trx). Revised sets overlap the 2026-10-05 baseline and are recorded by date rather than summed.

## 2026-10-06 second detail interaction revision

The detail editor now has two views, **Rich text** and **AegiSub code**. UI labels use **Highlight** while existing localization keys and persisted karaoke enum values retain their identities. Enable highlight adds the character axis beneath the rich text editor. Selecting a clip opens a popup for Snap, Duration, Leading gap, and Highlight mode; the persistent Current clip display and separate timing section are removed. Positive clip durations may extend beyond the cue end, with the overflow remaining editable and the cue boundaries unchanged. Timeline snapping and its temporary Alt bypass remain available.

The top **Highlight style** toggle changes the target of the shared style controls. Body mode edits the text selection. Highlight mode edits color, outline, and shadow only, keeping body font, size, emphasis, and layout intact and disabling their controls. The shared preset selector switches to highlight presets, and selection applies the preset immediately. Body-selection and highlight drafts retain separate ownership even though their controls are shared. There is no independent highlight parameter section or Apply highlight action.

The three icon actions apply a body preset to the selection, apply the edited body selection style, or clear the selection's local styles. They target only body text selections and are unavailable in highlight mode. Play/pause remains one icon button beside Loop, Enable highlight, and the highlight target toggle. The header and style toolbar stay fixed while property groups can collapse and the text/code area scrolls. Complete field groups still wrap at narrow widths, and the X/Y vector inputs remain on one row.

Valid highlight fields commit on Enter or focus loss; invalid raw input remains editable, and Escape restores that field. Preset selection does not require another Apply button. Clip duration must be positive and the leading gap nonnegative. Reset restores the pending detail drafts. Switching the shared style target or view preserves the existing validation boundary and does not discard invalid drafts.

The focused Desktop model/workflow set passed **29/29 with zero skipped**, covering body/highlight drafts, detail layout migration, highlight editing coordination, and nine added parameter cases; see [model TRX](../../artifacts/verification/details-highlight-model.trx). The final Headless UI regression set passed **69/69 with zero skipped**, including 28 detail cases plus snapping, overflow, disabled-color geometry, layouts, windows, localization, and numeric drafts; see [UI TRX](../../artifacts/verification/details-highlight-regression.trx). Both language packs contain 546 matching keys.

The strengthened regressions cover four interaction protections: calling the real `Popup.Close` cancels dismissal for an invalid draft while retaining the same popup root, child, focus, and playback; save validation focuses the precise invalid style field; a rejected highlight preset selection restores the displayed preset; and clip selection refreshes the toolbar actions immediately. The real-close test first failed **1/1**, preserved in [popup-close red TRX](../../artifacts/verification/details-highlight-popup-close-red.trx), and then passed within the final 69-case set. The red run is not added to the green count. The [clip popup capture](../../artifacts/verification/details-toolbar-clip-popup.png) shows the fixed toolbar, the two views, same-row X/Y inputs, and the popup above the character axis. Visual review confirmed four clear popup inputs in a two-by-two arrangement and enabled body style actions after selecting the first character.

These focused sets overlap the earlier records and are not added to their counts. The full content and media suites were not rerun for this revision. The latest Rider checks of five changed core files reported no ordinary WARNING/ERROR; WEAK WARNING style suggestions remain. The final independent Desktop and worker build after the interaction fixes succeeded with zero compiler warnings/errors, and `git diff --check` passed. Headless coverage and the capture do not replace native/manual acceptance of the new popup, IME candidate UI, or audible playback.
