# Subtitle editing

[English](subtitle-editing.md) · [简体中文](../zh-cn/subtitle-editing.md) · [All guides](README.md)

## Tracks and compositing

A track can contain subtitles, images, and shapes. Clips on the same track cannot overlap; put overlapping clips on separate tracks. The top track appears in front in both preview and export.

Drag a track header to change its order. Its clip and animation rows move together; the insertion line shows the destination. Release commits one undoable change, which is saved with the project. Escape cancels the drag. Solo and fold buttons keep their own actions.

## Timeline clip timing

Move subtitle clips and trim either edge in 1 ms increments by default; trimming keeps at least 1 ms of duration. **Step** aligns edits to the visible ruler divisions. With snapping enabled, either clip edge can snap to another clip boundary or the current red playback line, using the target's exact time. Hold Alt to temporarily bypass Step and snapping.

Extending a clip to the left preserves existing effect keyframes at their original project times. You can move or add keyframes throughout the expanded clip. Each completed drag commits once and can be undone.

With a clip selected, press **Shift+Q** to seek to its start or **Shift+W** to seek to its end. Multiple selection uses the primary clip. Seeking preserves playback state and zoom, and brings an offscreen target into the visible timeline. Text inputs retain uppercase Q/W entry. Rebind or disable these commands in **Settings → Shortcuts → Playback and audition**.

Select multiple rows in the subtitle list or clips in the timeline, right-click **Move**, enter an integer number of milliseconds, and click **Confirm**. Positive values move later; negative values move earlier. The same offset shifts both boundaries of every target, preserving duration, spacing, and internal animation time. The subtitle list moves selected subtitles; the timeline moves the entire selected clip set. Cancel or 0 leaves timing unchanged. Moving any target before zero or creating overlapping clips on the same track rejects the whole batch. One Undo restores the batch.

## Rich text and karaoke

Select a subtitle and open **Subtitle Details** from View or the subtitle row. The panel can dock or float and offers rich-text, highlight-timing, and advanced-code views of the same cue.

1. Select text and use the shared style controls for local font, size, emphasis, fill, stroke, or shadow.
2. Enable highlighting to edit grapheme-based karaoke clips. Drag their boundaries, keep durations positive, and use a nonnegative leading gap.
3. Turn on **Highlight style** to edit highlight fill, outline, and shadow; selecting a preset applies it immediately.
4. Use **Reset** to discard pending content, style, and timing drafts.

The optional Duration display shows all clip lengths; otherwise labels appear during dragging. Timing can extend beyond the cue, shown as a red overflow area, without changing cue boundaries. Disabling highlighting retains its clips for later editing.

Valid drafts commit on Enter or blur; invalid text stays editable, and Esc restores the field. Text edits preserve unaffected clip identities and timing. Each completed style/timing operation has one Undo.

Set alignment in the **Styles** panel using two groups of icon buttons: left/center/right and top/middle/bottom. Changing one direction preserves the other, and the same control is available in the style library. The combined value matches ASS `\an1`–`\an9`; editing that tag in the code page updates the style panel. Explicit position stays controlled by the position editor. Older projects retain their independent text alignment until you actively choose an alignment, which clears that legacy override.

The Styles panel and Settings → Styles use the same four groups in the same order: **Typography**, **Fill and outline**, **Shadow**, and **Layout and position**. All groups stay expanded. Font selection shares a row with Bold and Italic icon toggles on its right, followed by Size and Line height together; fill and outline each have their own row. Layout uses one diagram for margins and position. Settings also retains the preset name, sample preview, and library toolbar.

**Letter spacing** adjusts the gaps between shaped text clusters and accepts negative values while keeping combining characters and ligatures intact. **Fill blur** and **Stroke blur** control the two edges independently of shadow blur and whole-layer blur. Edit these values in Styles, selected text, or the style library; they also support native animation and effect scripts. Highlight styles expose the two edge blurs, while body typography controls spacing. Animated spacing updates canvas bounds, text hit testing, and placement together.

**Wrapping** applies to the whole line: **Grapheme** retains the existing layout; **Natural** prefers Unicode line-break opportunities and preserves nonbreaking-space joins; **No wrap** breaks only at explicit newlines. Migrated projects and style libraries retain grapheme wrapping, zero spacing, and zero fill/outline blur, preserving their existing appearance.

Choose **Automatic** or **Custom anchor** in the positioning row. Automatic layout hides the anchor, pivot, and offset inputs while keeping the preset grid and diagram visible; choosing a preset enters custom mode. Switching modes preserves layer position effects. The Styles panel's reset icon restores automatic layout and clears position offsets, motion paths, and position animation in one undoable edit. Settings uses the mode selection without a duplicate reset action.

**Margins** has separate **Left**, **Right**, and **Vertical** values in canvas pixels, in both the Styles panel and Settings → Styles. Left and Right define the wrapping region; horizontal centering uses the center of that region, so unequal values shift centered text. Vertical controls top/bottom placement and is ignored for middle alignment. Explicit position controls placement and disables the Vertical field, while Left and Right still limit wrapping. Margins describe text layout; stroke and shadow may extend beyond it, and existing vertical baseline placement is preserved. The shared position diagram shows the layout region and automatic/explicit position. Invalid input stays editable and blocks a new preview, save, or apply; Esc restores only the focused field.

## Audition a segment

Use the header play/pause button to audition the selected clip, or the whole cue if none is selected. **Loop** repeats the range and defaults off. Changing Loop while paused does not start playback.

Leaving the whole panel or deactivating its floating window pauses playback. Changing the target, closing the panel, or seeking in the main window cancels the previous range. Audio and video play together.

## Import and export

| Menu | Format |
|---|---|
| Format → Aegisub → Import / Export | ASS styling, karaoke, and supported masks |
| Format → SRT → Import / Export | Text and timing |

Imports create independent tracks; overlapping cues keep their times on additional tracks. One Undo removes the batch. ASS PlayRes maps to the existing project canvas. Invalid structure, times, dimensions, UTF-8, or resource limits reject the import.

ASS supports base/local styles, resets, colors/alpha, outline/shadow, alignment/position, whole-line static scale and Z rotation, `\move`, `\fad`/`\fade`, `\k`, `\kf`/`\K`, `\ko`, rectangle/vector clips, and supported rectangle transforms. Imported position and opacity animation uses native tracks, editable with Undo through the canvas, Effects panel, and timeline. Ordinary fades use keyframes; envelopes with instantaneous jumps use ordered transforms, edited through the Effects panel's transform operations.

Export preserves representable transform parameters, linear motion, and opacity envelopes with up to two transitions. Recognized variable-speed straight motion and eased fades report their constant-speed/linear approximation. Unsupported animation is omitted from the output without modifying the project. Opacity envelopes preserve each fill, outline, and shadow alpha. The project applies opacity after composing a layer, while ASS applies fading within the text, so overlapping pixels can differ. Font metrics, scaled wrapping, and nonuniform outlines also have visual differences; parameter correspondence does not imply pixel equivalence.

Conversion review identifies each subtitle by sequence, time, text, and specific loss, with copyable details. Cancelling an import preserves the project; cancelling an export preserves an existing destination. No compatibility mode is required.

ASS style `MarginL`, `MarginR`, and `MarginV` map independently to these three margins. Nonzero dialogue values override the corresponding style value; zero inherits it. Import scales Left/Right by the PlayRes width ratio and Vertical by the height ratio. Export writes the three values separately, keeping ordinary margin placement automatic and preserving explicit `\pos` placement. Export uses `WrapStyle: 1`; font metrics and wrapping can still differ from an ASS player.

Exports include all subtitle tracks. SRT reports loss of rich styling, highlighting, masks, and animation. ASS uses track order for layers; project times remain exact until millisecond SRT or centisecond ASS output. Unrepresentable positive karaoke durations reject export. Files use UTF-8 and atomic writes.

## Merge projects from multiple contributors

When contributors time separate sections against the complete original video, choose **File → Merge other projects…** in the main project and select one or more `.aeginext` files.

The merge preserves source times and adds independent “project name / track name” tracks, retaining their order and default styles. Subtitles, rich text, karaoke, shapes, images, animations, masks, and project effect presets are imported together. Each source is placed above the main project, and later selected sources appear in front; each source retains its internal track order.

The main project keeps its video, canvas, frame rate, reference white, and timeline view. Sources must have matching canvas dimensions and reference white; differing frame rates do not change exact subtitle times. This workflow expects projects already aligned to the full original video and does not infer or shift segment times.

Referenced fonts and images are copied into the main project's `assets/`, reusing identical content; videos are not copied. Deliver the source project's managed fonts and images together with its project file. System fonts still need to be installed on the merging computer. Missing resources, hash mismatches, invalid projects, or exceeded budgets reject the entire batch. Copy backups back into their source project root before importing them.

One Undo removes the entire selected batch, and Redo restores it. Resource files remain available for history. Save the main project after review to retain the merged content when reopening it.

## Masks and advanced code

Create rectangle or closed multi-contour Bézier geometry in **Masks**, then edit it in Preview. Masks use project coordinates and crop only their Clip, including text, stroke, shadow, karaoke, and its blur. Subtitle transforms do not move the mask; its own transform does. Inversion and nonzero-winding holes are supported.

Advanced code includes the selected Clip's mask. Compatible edits preserve node identities and mask tracks; node morph animation requires clearing node tracks before changing topology. Text, mask, and animation commit together.

ASS writes exactly representable rectangle animation as `\t`. Other mask animation expands to frame-sampled static events, with diagnostics for sampling and coordinate rounding. Such karaoke uses absolute `\kt` timing; the target player must support it. Export is limited to 100,000 dialogues.
