# Subtitle workbench

[English](workbench.md) | [简体中文](zh-CN/workbench.md)

The workbench has seven fixed panels—Preview, Timeline, Subtitles, Styles, Effects, Export, and Log—inside composable dock space. Drag to reorder, split, tab, hide, or float them. Layout offers Standard, Timing, Effects, Export, and personal presets; current space is remembered automatically. Settings opens from the menu. See [workspace ownership](composable-workspace.md) and [title/color/encoding evidence](checkpoints/export-title-colors.md).

The main title is `AegiNEXT - Project name •`; the dot appears only when dirty. Names come from the saved project filename, explicit unsaved name, video filename for an unnamed project, or localized Untitled. Successful Save As refreshes the title and next suggested filename; failure/cancellation does not. Native and custom titles stay synchronized, and floating titles append the active panel. This projection does not mutate project data or Undo.

The first version uses .NET 10/Avalonia, `.aeginext` projects, one background video, subtitle tracks, and effects. UTF-8 SRT/TXT import and SRT export are available; ASS is a later adapter.

## Playback and preview

A compact transport has 32×32 play/pause and mute/unmute icon buttons, centered time, and a volume slider. Mute preserves volume. Tooltips describe actions. Progress and transport use a small gap; the dedicated Slider theme retains a full 20×20 thumb and centered track.

Preview has no duplicate inner title. File information stays in the picture hint; the SDR/HDR badge is fixed at upper right and does not intercept editing input. Composed editing output is SDR, regardless of source HDR tags.

The quality selector and SDR/HDR badge share one 32-DIP overlay row. Dragging either the timeline playhead or preview progress bar requests video frames while the pointer is held, including a return to the starting position. Release performs an exact seek and restores the selected preview quality. Scrubbing uses a paused preview; it does not start normal playback.

Select preview quality: Low **540p (960×540)** is the default, Standard is **720p**, and High is **1080p**. Small videos are not enlarged. The choice is a personal preference, independent of project data, and is restored after restart. During interaction the cap is 540p and never exceeds the selected quality; release restores that choice. Quality does not change export resolution or HDR processing.

Dock titles/content/floating panels use shared rounded corners and theme borders. Layout owns the outer frame; panels do not draw duplicate outlines. Tabs have no filler dividers or item borders: selection uses accent text and a translucent background.

## Tracks and subtitle editing

File commands cover project creation/open/save, media open, and import/export. Menus and buttons share commands and availability. macOS defaults to the system menu; window-menu mode puts menus only on the main workbench.

Click a track heading or empty area to select only the track, clearing clip/keyframe selection. Add, timing, and import target that track. Click a clip to select its track. Context menus manage creation, rename, order, collapse, and empty-track deletion. Subtitles places its track filter beside Add/Delete/Split/Merge. Tracks organize clips without changing composition order. Same-track overlap is forbidden; cross-track collision/import is atomically rejected without retiming.

Assign a preset through the target track's context menu. A badge below its heading displays the saved preset name. Changing the preset on a nonempty track asks whether to change existing clips:

- **Yes:** update existing clips and the default for future clips.
- **No:** preserve existing clip styles; update only the track's saved default.
- **Cancel:** make no change.

No is the safe default. There is no apply-to-all-tracks action. Automatic track styling can be disabled; the assigned snapshot remains saved, while new clips use the preset currently selected in Styles. Reenable it to use the track default for future clips. Preset names/IDs and project-local snapshots are saved; personal-library changes/deletion do not rewrite the project. Font preparation and changes commit in one transaction, preserving times, effects, and highlighting. Split and cross-track moves retain each clip's style.

Space plays/pauses. F8 always creates a new sentence; F9 only finishes the still-valid segment from that operation. Held F8 does not repeat creation. Editing, clicking, scrolling, seeking, other commands, or window changes invalidate the segment. Collision rejection preserves the project and timing state. Time fields accept seconds or `hh:mm:ss.fff`; blur commits valid text, while invalid drafts remain with errors.

Drag a clip body to move it, an edge to trim, or Ctrl+edge to stretch time explicitly. Snap defaults on and aligns to other clip boundaries. Step quantizes to the viewport's smallest division. Without Step, dragging uses project-frame precision; Alt bypasses Snap/Step and uses milliseconds. A completed gesture creates one Undo. Edge hover/drag uses a horizontal resize cursor.

The minimap shows viewport and playhead. Drag its viewport, edges, or click to navigate without seeking. Wheel scrolls tracks; Shift+wheel pans; Ctrl/Cmd+wheel and pinch zoom around the pointer; horizontal touchpad gestures pan.

The single triangle in a track heading expands/collapses all its existing animation-property rows. There is no separate diamond expansion button. Collapsing keeps the subtitle clips visible and does not remove animation. The track name and style badge use the shared mixed-text font chain and centered line boxes.

To split, place the text caret and playback position inside the sentence. Split inherits its track. Merge requires same track and compatible timing, layer order, and effects; it never silently discards differing effects. Save/Save As/Open use Cmd/Ctrl+S, Shift+S, and O; outside text editors, Z and Shift+Z undo/redo.

## Styles and highlighting

The searchable font selector lists system and project fonts. Set size, fill, stroke, bold/italic, and alignment. Text grows naturally. Explicit position combines normalized canvas Anchor, actual-ink Pivot, and pixel Offset, with X right and Y down. Each uses one X/Y vector row. Anchor/pivot range is 0–1. Presets preserve actual text position; Shift also sets pivot, Alt also zeroes offset. There is no Rect/Stretch/size/margin-box model. Esc restores only the current component.

Styles and Effects offer Restore Automatic Position. One transaction clears explicit anchor positioning, local translation, position tracks, path, and path-progress animation, preserving other effects. Repeated restoration does not add Undo. Alignment and measurement share actual ink bounds: centered offset X is exactly zero, left/right use signed margins. Switching automatic to explicit preserves pixels.

Highlighting distributes timing by Unicode grapheme. Choose Highlight Style and apply; one appearance snapshot stores fill/stroke/width/shadow while retaining the sentence font, size, and geometry. Existing character timings survive appearance changes. Default Highlight keeps legacy behavior; Clear removes segments and snapshot. Merely changing selection/language/theme does not transact the project.

## Effects and animation

Select clips in Timeline; Effects has no separate layer list, duplicate preview, or subtitle-name draft. Start/end fields are labeled, and empty error/target status does not reserve space.

Expanded tracks continuously show every clip's existing animated properties above the corresponding clip. Rows use the property union; position/scale components share a row. Names stay visible; numeric keyframe labels appear only on hover, after curve clipping, within the visible track. Add at the playhead; a new time adds a point and the same time updates it. Select a keyframe to pause/seek and edit value/interpolation; changes to other animated properties use the same time. Without a fixed keyframe target, animated properties edit at the playhead and unanimated ones edit the base value.

Real keyboard drafts commit before target switches; invalid input remains and blocks that switch. Esc restores the current field. Playback/seeking/navigation stay available; an invalid draft retains its original target. Active playback or manual seeking otherwise exits the fixed target. Drag time/value within the clip. Interpolation supports hold, linear, ease-in, ease-out, and ease-in-out.

Builtin fade/pop/slide options are [DSL scripts](effect-dsl.md), recompiled for clip duration. Settings manages/imports/exports scripts with token highlighting, contextual completion, and line/column diagnostics; the workbench's Apply Preset is the application entry. Personal script libraries are separate from projects/layouts.

Position/scale each use one Vector. X/Y markers remain at real times without horizontal offsets. Coincident points merge into a yellow/white marker; shared vertical dragging preserves component differences. Fill/stroke each use full RGBA, with RGB and separate Alpha regions in one row.

Cubic Bézier paths are edited in Preview. Buttons or canvas double-click append endpoints; removing the last endpoint retains a minimum path; clearing also removes progress animation. Shapes, images, groups, and masks have no workbench tools, although underlying scene data remains validated/rendered. A video gesture pauses and freezes its target, commits one Undo on completion, and cancels on capture loss/selection change.

The left timeline toolbar controls Snap/Step and independently hides energy/waveform. Successful snapping draws a boundary outline; Alt, invalid placement, clamping away from the boundary, release, and cancel clear it. Display switches preserve analysis, viewport, playhead, and the fixed panel across docking/floating. Light/dark palettes cover tracks/text/clips/curves/markers, with a subtle animation-row background for readability. Rulers refine to milliseconds.

Trim preserves interpolation phase and inserts boundary points. Move translates displayed clip/keyframe times. Stretch scales keyframe/path/karaoke time. Points stay within visible content, including the exact end boundary, while playback uses half-open clip intervals.

## Storage and resources

Strict project JSON v3 stores rational time, tracks/TrackId, Anchor/Pivot/Offset, full vector/color animation, and trimmed curve intervals. v1/v2 projects are unsupported. Layout/shortcut migration is separate. Save writes a same-directory temporary file then atomically replaces; load validates required fields, references, overlaps, ranges, budgets, and rejects unknown/duplicate fields.

Videos inside the project directory use canonical relative references; videos outside it or with paths that cannot be represented portably use absolute external references. Videos are never copied. Relative video input resolves against the current project directory, which is the session scratch directory before the project is saved. Every Save and Save As reclassifies video references against the destination directory while retaining the original source, including legacy absolute references during ordinary Save. Moving the whole project directory together with its videos preserves relative references. Imported fonts use managed resources with optional SHA-256. Save As relocates resources and rechecks hashes; managed relative paths cannot escape the project directory. Cross-directory relocation clears history that could restore stale relative paths; same-directory Save retains it without adding a save transaction. Reopening verifies media streams, canvas dimensions, and time origin against actual media; unknown starts use the explicit zero-origin policy.

## Settings, input, and logs

Settings has Appearance, Colors, Shortcuts, Subtitle Styles, and Effect Scripts. Appearance offers System/Light/Dark, System/Chinese/English, and menu mode. Valid changes apply/save immediately. Accent lives in Colors. Shared spacing/heights/fonts apply to business inputs, with 13 px tab icon labels. Volume persists; mute is session-only. `AEGINEXT_PREFERENCES_DIRECTORY` isolates test preferences.

Colors offers Classic/Ice/Ember/Grayscale and low/mid/high-energy plus waveform fields. Builtins adapt to light/dark; Classic dark preserves the original spectrum mapping, and light uses a bright base with dark energy. Manual/custom input disables adaptation and retains entered colors; energy interpolates between three stops. Accent/energy are opaque; waveform supports alpha. Preview and actual timeline share the palette resolver. Changes do not reanalyze, move the viewport, or affect Undo. Missing old preference fields default to Classic. Stable item identities permit direct return to Classic; language/theme updates preserve drafts. Restore Default Colors resets only accent/audio colors and their drafts, applies/saves once, and preserves all other preferences/project data.

Shortcut recording captures actual chords in a read-only display, rejecting text/paste. Validate format and real collisions; the selected command's own chord is not a conflict. `CmdOrCtrl` maps to the platform modifier. Recording consumes down/repeat/text/up through full release; Esc cancels, blur/close clears state. Text editing retains Space/arrows/delete/text Undo, while F8/F9 can time subtitles. Global means application-wide, not OS-wide hotkeys. Menus/tooltips reflect the actual binding. Focused closed menus do not block globals; open menus/dropdowns/modal dialogs retain local keys. Registered windows consume matching releases/repeats.

macOS system-menu roots persist per registered window; changing focus does not clear them. Window menus appear only on the main workbench. Theme/shortcut updates are in-place; language regrouping occurs at safe menu lifecycle points. See [windowing](workspace-windowing.md).

Font search confirms with selection/Enter/blur; Esc restores the confirmed font. Search text itself does not edit subtitles or Undo. Settings styles can create/copy/edit/delete/capture/apply presets; Styles can apply to the current subtitle. `.aegistyles` includes position, imported font bytes, and hashes. System-font presets require installation on the destination. Import validates the entire library and rejects duplicate names/IDs. Font application imports target-project resources transactionally. Budgets: 256 styles, 32 MiB/font, 64 MiB total fonts, 96 MiB preset file. Only the style page is disabled during asynchronous save/import to protect drafts.

Log is a dockable/floating/hideable panel and a nonactive bottom tab in Standard. Errors do not steal focus; View shows unread errors. The session retains 2,000 operation/warning/error records, with level/text filters, copy, and clear. Repeated media errors log only on state changes. Expand selectable exception text per record; there is no separate bottom details textbox. Logs do not persist across restart.

## Media and text boundaries

Playback uses FFmpeg and SDL3, with roughly 200 ms prefill and a 250 ms queue cap. Position estimates submitted minus queued samples and one device buffer; this is not an exact DAC clock. No audio/device failure still permits video and reports the audio issue.

Analysis independently reads 16 kHz mono PCM and builds bounded waveform/log-frequency energy, up to six hours, without consuming playback audio. Preview derives SDR; export uses original frames, as described in [export](export.md).

Basic Chinese/Japanese/Latin, line breaks, and unidirectional paragraphs are supported. Full mixed bidi and per-run fallback remain boundaries. Shared business text line boxes align mixed text; DSL keeps monospace/top alignment and long logs stay top-aligned.

All colors use a continuous 32×32 swatch/text/mode/picker row. HEX `#RRGGBB`/`#RRGGBBAA` and comma-separated 0–255 sRGB RGBA are linked to the picker. Enter/blur commit, Esc restores, errors remain local. Untouched HDR values survive formatting/language/theme/mode changes; DSL `rgba(...)` remains linear.
