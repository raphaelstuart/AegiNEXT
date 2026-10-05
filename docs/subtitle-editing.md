# Subtitle detail editing and format exchange

[English](subtitle-editing.md) | [简体中文](zh-CN/subtitle-editing.md)

The subtitle list always displays readable text. Its derived Type column shows Highlight when character timing exists, Rich text when local style overrides exist, and Plain otherwise. Highlight and rich text share one content model, so a highlighted subtitle can also contain different fonts, colors, and emphasis.

## Open, float, and dock

Select a subtitle and use **Detail editor** on the subtitle toolbar or row context menu. The editor opens as a workspace child window. Drag its panel tab to dock it into the main workspace or float it again. Reopening activates the same panel, which follows the primary subtitle selection. Double clicking list text still selects words.

Layouts remember the detail panel's position and visibility. Properties use complete groups with a muted title above the input; paired X/Y inputs stay on one row. Narrow windows wrap whole groups, and property groups can collapse to retain text-editing space. The header and style toolbar stay fixed while the text and code areas scroll. Numeric fields hide spinners and preserve exact unedited values. Closing the detail panel or switching layouts validates pending drafts; invalid input must be corrected or explicitly discarded with Reset. Existing layouts migrate with the detail panel hidden and retain their previous topology.

## Two editing views

- **Rich text** edits the displayed text directly. Select text before applying font, size, bold, italic, underline, strike through, color, outline, or shadow. Toolbar focus retains the selection. The three selection icons apply a preset, apply the edited style, or clear local styles only within the body text selection. The existing whole-line Apply style command replaces the base style and clears local overrides while preserving highlight timing.
- **AegiSub code** edits supported ASS tags with source-to-text selection mapping. This view edits content and styles. Project position remains owned by the native position editor and effects; an entered `\pos` tag reports a diagnostic and must be removed. Invalid code remains a draft while preview retains the last valid content; correction or restoration is required before changing the target/view, closing, or saving. Text that cannot be represented losslessly as native ASS disables only this view and displays a compatibility explanation; ordinary project editing and saving remain available.

Enable highlight displays the character axis beneath the rich text editor and distributes cue time across complete graphemes. Each Chinese character, complete emoji, or combining sequence has a separate clip. Imported multi-character clips retain their total time when divided into graphemes. Disabling highlight removes character timing and retains the highlight configuration for reuse.

Selecting a clip opens a popup containing Snap, Duration, Leading gap, and Highlight mode. Drag its right edge to resize it; later clips shift together, and the cue boundaries stay fixed. Snap defaults on and aligns drags to a 10 ms grid, original boundaries captured at gesture start, and the cue end. Its toggle does not edit the project; hold Alt to disable it temporarily. The leading gap creates space before the sentence, and remaining cue time forms trailing space. Existing gaps and cropped timing retain their original meaning.

The top **Highlight style** toggle switches the shared style controls between body selection styling and highlight styling. In highlight mode, only color, outline, and shadow are editable; font, size, emphasis, and layout stay with the body and their controls are disabled. The three body selection actions are unavailable in this mode. The shared preset selector shows highlight presets, and selecting a preset applies it immediately. Highlight settings use the shared controls, with no separate highlight parameter section or Apply highlight button. The Styles panel continues to edit body styles.

Highlight field edits commit on Enter or focus loss. Invalid raw input remains editable, and Escape restores that field. Clip durations can extend beyond the cue and remain committable without changing cue boundaries. A red region to the right of the axis displays the overflow for further editing. Durations must remain positive, and the leading gap must be nonnegative. Escape in text/code restores pending content. Reset discards pending content, style, and timing changes.

View changes preserve styles and timing. Continuous typing is grouped into one undo operation at a commit boundary. A body style application, highlight preset selection, valid highlight field commit, enable toggle, or completed drag has one undo entry. Text changes redistribute timing only in the affected region and preserve other clip identities and times. Carets, selections, and IME composition respect complete Unicode graphemes.

## Segment playback

The header has one play/pause button and a Loop toggle. Loop defaults off. Play auditions the selected clip, or the entire cue when no clip is selected. Click the same button to pause. Loop can change during playback; changing it while paused never starts playback. Moving focus to its toolbar keeps playback running; leaving the whole panel or deactivating its floating window pauses immediately. Refocusing does not start playback. Changing/deleting the target, closing the panel, resetting the project/media, or seeking in the main window cancels the previous loop. Audio and video use the same bounded playback path.

## ASS and SRT

Use **Format → Aegisub → Import / Export** for native `.ass` and **Format → SRT → Import / Export** for text and timing in `.srt`. Video export stays in File. Existing subtitle import/export shortcuts retain their SRT meaning; ASS shortcuts are initially unbound and can be configured in settings.

Each import creates independent tracks. Overlapping cues retain their times and occupy additional tracks. One undo removes the whole batch. ASS coordinates use the source PlayRes and map to the current canvas without changing project dimensions. Missing dimensions prompt a diagnostic. Invalid dimensions, times, structure, UTF-8, or resource limits reject the entire import.

ASS supports base/local styles, style reset, color and transparency, outline/shadow, alignment/static position, and `\k`, `\kf`/`\K`, and `\ko`. Unsupported tags and animations identify the affected subtitles and require Continue conversion. `\move` is not converted into a project effect; project motion remains the effect system's responsibility.

Export includes every subtitle track. SRT uses stable time sorting and keeps overlaps as separate entries; loss of rich styling, highlight timing, or effects requires confirmation. ASS derives layers from the actual scene order. Projects keep exact times, with milliseconds for SRT and centiseconds for ASS only at the output boundary. Highlight timing uses cumulative boundary quantization; export fails if positive durations cannot be retained. Strict UTF-8 supports input BOMs and writes through a same-directory temporary file with atomic replacement.
