# Timing post-processor

[English](timing-post-processor.md) · [简体中文](../zh-cn/timing-post-processor.md) · [All guides](README.md)

## Save a style association

1. Open **Settings → Timing processor**.
2. Select a style row to load its saved options, or edit candidate options.
3. Check the target presets and choose **Save association** to apply that configuration to all of them.
4. Use **Unlink** to remove associations from checked presets.

Styles come from the personal library, so no project or video is needed. This page saves configuration; automatic execution and style-application triggers are not connected to project editing yet.

## Default options

| Stage | Default |
|---|---|
| Lead-in / lead-out | Enabled, 100 / 350 ms |
| Adjacency | Enabled, max gap 300 ms, max overlap 50 ms, end bias 90% |
| Keyframe snapping | Enabled; start before/after 200/150 ms, end before/after 200/250 ms |

The engine runs these stages in order. Thresholds are inclusive; 0% bias adjusts the next start, 100% the previous end. Executing keyframe snapping requires a real video-frame index.

Enter or blur commits valid integers; invalid drafts stay editable, and Esc restores the current field. Candidate settings remain independent until **Save association**.

## Persistence and processing

Associations follow stable style IDs: rename preserves them, copy duplicates them, deletion removes them, and `.aegistyles` carries them. Style library v4 reads v1–v3; older applications cannot read v4.

The engine uses rational time and per-track adjacency. Invalid durations or final collisions reject the whole result; crop semantics preserve animation phase. Frame indexing uses actual PTS, supports VFR/nonzero origins, and rejects broken timing or changed media/tool identities.
