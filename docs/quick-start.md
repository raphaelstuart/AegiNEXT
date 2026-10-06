# Quick Start

[English](quick-start.md) | [简体中文](zh-CN/quick-start.md)

This guide covers the AegiNext **0.1.0 development preview**, from opening a video to exporting subtitled output. The interface supports English and Chinese; menu names below use English.

## 1. Launch

Extract a complete development package for your platform and open:

- macOS: `AegiNext.app`. The current locally built Apple Silicon package requires **macOS 27.0+**; check the package manifest for the actual minimum.
- Windows: `AegiNext/aegi-next.exe`. The development target is **x64**, including on Parallels Windows 11 ARM64 through system emulation.

Keep the entire directory: it contains .NET, FFmpeg/FFprobe, audio, export, and native runtime dependencies. To run from source, see [building](building.md).

Normal startup opens the welcome window. Search recent projects by name or path, click an available project to open it, or use New Project, Open Project, and Settings in the top toolbar. Each project has a name abbreviation and a stable color. Right-click an entry to remove it from recent history; the project file remains on disk. Missing files appear dimmed and cannot be opened, but can still be removed from history.

New Project asks for an `.aeginext` save path. The file must be saved successfully before the workspace opens; cancelling keeps the welcome window visible. A new project can have no video. The full Settings window is also available before opening a project.

The workspace has Preview, Timeline, Subtitles, Styles, Effects, Export, and Log. Use Layout → Standard to restore the initial arrangement, and View to reopen hidden panels.

## 2. Open media and save a project

1. Choose New Project in the welcome window, select a path, and save the `.aeginext` project. Open Project can load an existing file.
2. Choose File → Open Video when you need to associate a video, and wait for Preview. Audio analysis runs in the background.
3. Use File → Save for later changes, or Save As for another path. File → New Project also requires saving the new file before replacing the current project.

File → Close Project or closing the workspace window returns to the welcome window after handling unsaved changes. File → Quit ends the application.

Projects store subtitles, tracks, styles, and animation. Video stays an external file reference and must remain accessible. Saving a project and exporting a finished video are separate actions. Project v3 is supported; v1/v2 are explicitly rejected.

The title is `AegiNEXT - Project name`, with `•` for unsaved changes. Projects opened through the welcome window use the project filename. Successful Save As updates the title; cancellation or failure leaves it unchanged.

Preview does not repeat the dock title. An SDR/HDR badge stays at the picture's upper right; the current composed editing preview is SDR. A square button controls play/pause, the time is centered, and a mute button and volume slider sit on the right.

Preview quality offers Low **320p (568×320)**, Smooth **540p (960×540)**, Standard **720p**, and High **1080p**. Smooth is the default. The choice is saved in personal preferences; small videos are not enlarged. Interaction is capped at 540p and never exceeds the selected quality, so 320p stays at 320p. Export resolution and HDR processing are independent of this choice.

## 3. Select a track and create subtitles

Click a track heading or empty track area to choose the destination track. Add and timing shortcuts use that track. Clicking a clip selects its track too.

Create, rename, reorder, collapse, and delete empty tracks through the timeline context menu. The Subtitles panel's top selector filters the visible list.

The triangle beside a track name expands/collapses all its existing keyframe-property rows while leaving clips visible. Dragging the timeline playhead or preview progress bar shows video frames before mouse release; release restores the selected preview quality.

Save reusable styles under Settings → Subtitle Styles, then choose a preset from the target track's context menu. The track stores a project-local style snapshot and its source name; subsequent changes to the personal library do not silently rewrite the project. Track styling preserves timing and effects, and is undoable. Split and cross-track moves preserve each existing clip's own style. The current track-style interaction is described in the [workbench guide](workbench.md).

### Add manually

1. Seek to the subtitle start.
2. Click Add at the top of Subtitles, or press `Cmd/Ctrl + Enter`; the default duration is two seconds.
3. Enter text and start/end times. Fields accept seconds or `00:00:12.350`.
4. Drag the clip body to move it, or its edges to trim.

Clips on the same track cannot overlap. A collision rejects the edit; adjust timing or use another track.

### Time during playback

1. Select a track and press Space outside a text editor to play.
2. Press `F8` when the sentence begins; it creates a new subtitle and records its start.
3. Press `F9` at the end to finish the current timing segment.
4. Enter text in Subtitles and review the picture and timing.

`F8` always creates a new sentence. `F9` only finishes a still-valid segment from that timing operation. Editing, selection, seeking, or switching windows invalidates the segment; start another one. If the OS captures function keys, use the Subtitle menu or change the bindings.

File → Import Subtitles accepts UTF-8 SRT/TXT into the current track. A same-track overlap rejects the entire import with a conflict report.

## 4. Style and position

Select a subtitle and edit its font, size, fill, stroke, alignment, and other properties in Styles. Text grows naturally with its content; font size is independent.

Explicit positioning combines a nine-position anchor preset, pivot, and pixel offset:

- Click a preset to preserve the text's actual position.
- Hold Shift to also set the pivot.
- Hold Alt to also zero the offset.

Drag text in Preview to move it. Restore Automatic Position in Styles or Effects returns it to style alignment and clears explicit positioning, manual translation, position animation, motion path, and path-progress animation. Font size, scale, rotation, opacity, and other effects remain; one Undo restores the operation.

Anchor, pivot, and offset each use one X/Y vector row. Anchor/pivot are normalized 0–1; offset uses pixels. Esc restores only the current component. Automatic alignment uses actual ink bounds: centered text has exactly zero horizontal offset, while left/right alignment uses style margins.

Color fields form one continuous row: square swatch, text input, square mode button, square picker button. Toggle HEX/RGBA with the mode button. HEX accepts `#RRGGBB` and `#RRGGBBAA`; six digits mean opaque. RGBA accepts four integers in 0–255, such as `255,255,255,255`. The picker expands the existing color selector and synchronizes text and swatch. Enter or blur commits valid text; invalid drafts stay visible with an error, and Esc restores the current field. Both display modes use sRGB. Merely changing mode, language, or refreshing does not quantize untouched HDR values.

Fill and stroke each use one full color animation; RGB curves share a row with a separate Alpha region. Alignment and Restore Automatic Position share one row. Use the preset selector and Apply Preset in Effects; settings only manage script templates.

Position and scale are complete vectors. Editing one component preserves the other. Their X curve is pale blue and Y is orange; horizontal dragging changes shared time, while vertical dragging changes the hit component. Coincident X/Y points use one yellow marker with a white border and move both components vertically. Values appear only while hovering a keyframe.

Settings → Subtitle Styles imports/exports `.aegistyles`, including position and imported fonts. Referenced system fonts still need installation on the other machine.

For per-character highlighting, select a Highlight Style preset in Styles and click the highlighting action. It uses preset fill, stroke, stroke width, and shadow while preserving the subtitle's font, size, and geometry. Reapplying to existing highlighting changes appearance without changing character timing. Default Highlight uses the legacy color. Clear Highlight removes segments and the style snapshot. The project snapshot remains usable after the personal preset is deleted.

## 5. Effects and keyframes

1. Select a subtitle clip in Timeline and open Effects.
2. Choose a fade, pop, or slide preset, then use Apply Preset.
3. Click ◆/◇ in the track heading to expand animation. Existing animated properties for **all clips** on that track stay visible, within each clip's time range.
4. Select a keyframe to pause and seek to it. Drag its time/value, or edit its value and interpolation in Effects.

Effects edits the timeline selection. Start/end fields have labels; subtitle content is edited in Subtitles. Keyframe times stay inside their clip. Editing another animated property while a keyframe is selected uses the same target time. Invalid drafts remain visible; Esc restores the current field. Resolve errors before changing editing targets; playback, seeking, and viewport navigation remain available.

Preview is the visual editing surface, with position handles linked to Effects. Undo/redo applies to these edits.

Settings → Effect Scripts manages [DSL templates](effect-dsl.md). Save As creates an editable personal copy of a read-only builtin. The editor has highlighting and caret-anchored contextual completion; type to trigger it or press `Ctrl/Cmd + Space`. Choose with arrows, insert with Enter/Tab, dismiss with Esc. Completion does not take focus and hides during IME preedit. Validate reports actual line/column diagnostics; Locate Error moves the caret explicitly.

Personal templates support rename/save/delete and UTF-8 `.aegifx` import/export. Applying recompiles for the current clip: fixed entrance/exit segments take priority, flexible segments share the remaining duration, and the script declares `compress` or `reject` for short clips. Templates are personal settings, independent of projects; only explicit Save updates them. Unsaved source survives template/language changes within the settings window.

Add Path Point in Effects, or double-click empty Preview canvas in path mode, to append an endpoint. Drag endpoints and white handles to shape the curve. Removing the last point retains at least two; Clear Path also removes path-progress animation. Shape, image, group, and mask editing have no workbench UI entry points.

The timeline's left toolbar has a magnet for Snap and a Step switch. Snap aligns moves/trims/keyframes with other clip starts/ends and draws a vertical outline at the accepted boundary. Step aligns to the viewport's smallest division. Alt temporarily bypasses both and hides the indicator. Snap defaults on; Step off. Without Step, dragging uses project-frame precision; Alt uses milliseconds. Zoomed rulers show milliseconds.

Separate toolbar buttons hide/show audio energy and waveform. They only affect drawing; reopening does not reanalyze, and docking/floating preserves the switches.

## 6. Save and export

Use `Cmd/Ctrl + S` during editing. File → Export Subtitles writes SRT containing all subtitle tracks.

To export video:

1. Open Export. CPU software encoding is the default, with speed and CRF controls.
2. Select automatic/H.264/HEVC and source-audio copy/AAC/no audio. Use GPU Hardware Video Encoding switches to video bitrate; subtitle composition still uses the high-precision pipeline. Unavailable hardware reports a specific error; uncheck GPU to use CPU. HDR uses CPU encoding.
3. Click Export and choose a file that does not exist yet.
4. Review progress, then inspect video, subtitles, and audio. Cancel stops the task.

Export uses the snapshot captured when started; later edits do not change the job. Cancellation/failure removes this job's temporary files. Editing is SDR; PQ/HLG export has a separate high-precision pipeline and HEVC 10-bit output. See [export restrictions](export.md).

## Default shortcuts

Use Cmd on macOS and Ctrl on Windows. Menus and Settings show your actual customized bindings.

| Action | Shortcut |
|---|---|
| New project | Cmd/Ctrl + N |
| Open project | Cmd/Ctrl + O |
| Open video | Cmd/Ctrl + Shift + O |
| Save / Save As | Cmd/Ctrl + S / Cmd/Ctrl + Shift + S |
| Play / pause | Space |
| Timing start / end | F8 / F9 |
| Add subtitle | Cmd/Ctrl + Enter |
| Undo / redo | Cmd/Ctrl + Z / Cmd/Ctrl + Shift + Z |
| Import subtitles | Cmd/Ctrl + I |
| Export SRT | Cmd/Ctrl + Shift + E |
| Export video | Cmd/Ctrl + E |
| Settings | Cmd/Ctrl + , |

Text fields retain text-entry Space; open menus/dropdowns and modal dialogs retain their own keyboard behavior. A focused but closed top menu does not block global F8/Space.

In Settings → Shortcuts, select a command, click Record, press the chord, release all keys, and Save Shortcuts. The gesture display rejects typing/pasting text. Conflicts identify the occupying command; recording the selected command's existing chord is valid. Esc cancels recording; Clear disables its binding.

## Navigation, layouts, settings

- Wheel scrolls tracks vertically; Shift+wheel pans horizontally; Ctrl/Cmd+wheel zooms around the pointer. Touchpads support pinch zoom and horizontal scrolling.
- The top minimap shows the project and viewport. Drag the viewport to pan or its edges to zoom, without changing playback position.
- Drag panel headings to dock, tab, split, or float. Layout offers builtins and personal presets. Current space autosaves; named presets update only with explicit Save. Active tabs use text/background highlighting without borders.
- Settings contains appearance, colors, shortcuts, subtitle styles, and effect scripts. macOS can choose system or main-window menus in Appearance.
- Colors contains accent and audio palettes: Classic, Ice, Ember, and Grayscale adapt to light/dark themes. Edit low/mid/high energy and waveform colors for a custom palette. Builtins can switch directly back to Classic; custom values display as entered. Valid input applies immediately, previews reflect the actual theme, and waveform supports alpha. Restore Default Colors resets accent and Classic audio colors, clears their drafts, and saves immediately, preserving other settings.
- Project edits require Save; automatic layout/appearance persistence does not save the project.

## Troubleshooting

View → Log provides operation history and full exceptions. Expand details to read/select text; filter, copy, or clear the log. Copy useful diagnostics before closing the session.

| Symptom | Action |
|---|---|
| Missing panel or disordered layout | Reopen via View, or choose Layout → Standard. |
| Cannot switch subtitle/keyframe | Correct the invalid field or press Esc in that field. |
| Add/drag/import rejected | Check same-track overlaps. |
| Missing project video | Restore the referenced video path and retain managed resources when moving the project. |
| DLL/native load failure | Reextract the whole platform package and copy the complete diagnostic. |
| Export cannot start | Check invalid drafts, media diagnostics, and output path; output must not exist. |

Continue with the [workbench guide](workbench.md) or [developer index](README.md).
