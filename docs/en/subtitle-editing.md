# Subtitle editing

[English](subtitle-editing.md) · [简体中文](../zh-cn/subtitle-editing.md) · [All guides](README.md)

## Rich text and karaoke

Select a subtitle and open **Subtitle Details** from View or the subtitle row. The panel can dock or float and offers rich-text, highlight-timing, and advanced-code views of the same cue.

1. Select text and use the shared style controls for local font, size, emphasis, fill, stroke, or shadow.
2. Enable highlighting to edit grapheme-based karaoke clips. Drag their boundaries, keep durations positive, and use a nonnegative leading gap.
3. Turn on **Highlight style** to edit highlight fill, outline, and shadow; selecting a preset applies it immediately.
4. Use **Reset** to discard pending content, style, and timing drafts.

The optional Duration display shows all clip lengths; otherwise labels appear during dragging. Timing can extend beyond the cue, shown as a red overflow area, without changing cue boundaries. Disabling highlighting retains its clips for later editing.

Valid drafts commit on Enter or blur; invalid text stays editable, and Esc restores the field. Text edits preserve unaffected clip identities and timing. Each completed style/timing operation has one Undo.

## Audition a segment

Use the header play/pause button to audition the selected clip, or the whole cue if none is selected. **Loop** repeats the range and defaults off. Changing Loop while paused does not start playback.

Leaving the whole panel or deactivating its floating window pauses playback. Changing the target, closing the panel, or seeking in the main window cancels the previous range. Audio and video play together.

## Import and export

| Menu | Format |
|---|---|
| Format → Aegisub → Import / Export | ASS styling, karaoke, and supported masks |
| Format → SRT → Import / Export | Text and timing |

Imports create independent tracks; overlapping cues keep their times on additional tracks. One Undo removes the batch. ASS PlayRes maps to the existing project canvas. Invalid structure, times, dimensions, UTF-8, or resource limits reject the import.

ASS supports base/local styles, resets, colors/alpha, outline/shadow, alignment/static position, `\k`, `\kf`/`\K`, `\ko`, rectangle/vector clips, and supported rectangle transforms. Unsupported tags/animation require confirmation before conversion; `\move` is not imported as a project motion effect.

Exports include all subtitle tracks. SRT reports loss of rich styling, highlighting, masks, and animation. ASS uses scene order for layers; project times remain exact until millisecond SRT or centisecond ASS output. Unrepresentable positive karaoke durations reject export. Files use UTF-8 and atomic writes.

## Merge projects from multiple contributors

When contributors time separate sections against the complete original video, choose **File → Merge other projects…** in the main project and select one or more `.aeginext` files.

The merge preserves source times and adds independent “project name / track name” tracks, retaining their order and default styles. Subtitles, rich text, karaoke, shapes, images, layer groups, animations, masks, and project effect presets are imported together. Source layers are appended in selection order, with each source's internal drawing order preserved.

The main project keeps its video, canvas, frame rate, reference white, and timeline view. Sources must have matching canvas dimensions and reference white; differing frame rates do not change exact subtitle times. This workflow expects projects already aligned to the full original video and does not infer or shift segment times.

Referenced fonts and images are copied into the main project's `assets/`, reusing identical content; videos are not copied. Deliver the source project's managed fonts and images together with its project file. System fonts still need to be installed on the merging computer. Missing resources, hash mismatches, invalid projects, or exceeded budgets reject the entire batch. Copy backups back into their source project root before importing them.

One Undo removes the entire selected batch, and Redo restores it. Resource files remain available for history. Save the main project after review to retain the merged content when reopening it.

## Masks and advanced code

Create rectangle or closed multi-contour Bézier geometry in **Masks**, then edit it in Preview. Masks use project coordinates and crop only their Clip, including text, stroke, shadow, karaoke, and its blur. Subtitle transforms do not move the mask; its own transform does. Inversion and nonzero-winding holes are supported.

Advanced code includes the selected Clip's mask. Compatible edits preserve node identities and mask tracks; node morph animation requires clearing node tracks before changing topology. Text, mask, and animation commit together.

ASS writes exactly representable rectangle animation as `\t`. Other mask animation expands to frame-sampled static events, with diagnostics for sampling and coordinate rounding. Such karaoke uses absolute `\kt` timing; the target player must support it. Export limits are 100,000 dialogues and 16 Mi characters.
