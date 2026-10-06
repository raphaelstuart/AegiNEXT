# Timing post-processor

Settings → Timing processor associates timing options with presets from the global style library, including custom styles created or imported in Settings → Styles. The list is independent of the open project and does not require loaded video.

Check target presets and choose Save association to save the same configuration for them in one operation. Selecting a style row loads its saved options. A check mark indicates an existing association. Unlink removes the checked presets’ configurations; unchecked presets keep theirs.

This settings page saves associations without editing project subtitles or adding undo history. Automatic execution timing remains to be confirmed; no automatic timing or style-application entry point is connected yet.

## Defaults

| Stage | Defaults |
|---|---|
| Lead-in / lead-out | Both enabled, 100 / 350 ms |
| Adjacent subtitles | Enabled, maximum gap 300 ms, maximum overlap 50 ms, end bias 90% |
| Video keyframe snapping | Enabled, starts before/after 200/150 ms, ends before/after 200/250 ms |

The processing engine runs lead-in, lead-out, adjacency, then keyframe snapping. Thresholds are inclusive. A 0% bias moves the next start; 100% moves the previous end. Saving keyframe settings does not require video; executing that stage needs a real frame index.

The stages follow the [Aegisub manual](https://aegisub.org/docs/latest/timing_post-processor/) and its [upstream implementation](https://github.com/Aegisub/Aegisub/blob/6f546951b4f004da16ce19ba638bf3eedefb9f31/src/dialog_timing_processor.cpp).

## Persistence and compatibility

Associations use stable preset IDs and an optional `TimingPostProcessor` field. Renaming retains them, duplication copies them, deletion removes them, and `.aegistyles` import/export carries them with the preset. Every selected ID and configuration is validated before one atomic write; failure preserves the previous snapshot, and an unchanged configuration does not write again.

Style library format 4 reads and migrates versions 1, 2, and 3. An absent optional association means unassociated. Present configurations require all fields and validation. Older application versions cannot read the new library format. The project format version remains unchanged.

Recently edited candidate options are application preferences, independent of each preset’s saved configuration. Editing them does not change a preset until Save association is chosen. Enter or blur confirms integer inputs, invalid raw text remains editable, and Escape restores only that field. Page and language changes preserve drafts. Association refreshes preserve unsaved style-page names and numeric inputs.

## Processing engine and video indexing

The engine preserves rational time and calculates adjacency per track. Nonpositive durations or final same-track collisions reject the entire result. Boundary edits use existing crop semantics to retain absolute content origins and animation phase. ASS import, preset application, and track defaults carry style names; older projects without this optional field use Default.

Fixed-version ffprobe scans actual display-frame PTS, including VFR and nonzero origins. Keyframe choices use frame-number distances, choosing the earlier candidate on ties. Ends use keyframe boundaries for half-open intervals. ASS 10 ms quantization and midpoint timestamps are not copied.

Scanning is cancellable and bounded to 2,000,000 frames, 10 minutes, and 128 Mi output characters. Missing, repeated, or regressing PTS, decode errors, exceeded budgets, or changed media/tool identities reject the index. Successful indexes are cached for the media identity and rescanned after identity changes.

Scoped domain, library, media, settings, workspace, and Headless UI tests live in their owning Tests projects. Reports and captures are under `artifacts/verification/timing-post-processor/`. Headless layout and real video fixtures do not establish native macOS or Windows acceptance.
