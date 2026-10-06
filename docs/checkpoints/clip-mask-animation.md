# Clip masks and animation

[English](clip-mask-animation.md) | [简体中文](../zh-CN/checkpoints/clip-mask-animation.md)

Date: 2026-10-06. The approved six phases implement one subtitle-only Clip mask model, common animation targets, shared rendering, ASS exchange, workbench editing and effect scripts. Existing working-tree changes remain; no commit was created.

| Phase | Delivered checkpoint |
|---|---|
| 1 | ClipMask replaces LayerMask; stable contour/node identities; project v5; v3/v4 null-mask migration and identified rejection of nonempty legacy local masks; presets no longer copy geometry. |
| 2 | Complete track targets, independent nodes/handles, shared metadata, POWER and stable-ID ordered transforms; content-clock crop/split/stretch; evaluated masks shared by preview and worker. |
| 3 | Atomic ASS import/code editing; native rectangle/inverse/vector clips, drawing scale and cubic normalization; four transform forms and ordered overlap; frame expansion, centisecond merging and loss diagnostics. |
| 4 | Independent Masks Dock and Preview gestures; square icon toolbar, vector coordinates, contour/node list, fixed-pivot transforms and relative handles; static subdivision and Ctrl-click deletion; topology lock, drafts and time-only timeline gestures. |
| 5 | Rectangle/transform/node DSL targets, one-based selectors resolved to stable IDs, power(exponent), atomic missing-target errors, editor completion/highlighting and bilingual examples. |
| 6 | Scoped tests, actual Headless mouse/keyboard operations, independent worker exports, Release build, Rider error analysis, project-copy round trips, preserved changes and reviewable visual fixtures. |

Masks operate in project-picture coordinates independently of subtitle/parent transforms. Rendering draws the complete subtitle and its own blur on a linear premultiplied F16 surface, crops that contribution, then composites into the parent. Multiple contours preserve direction and use nonzero winding. Inversion is bounded to the project picture. Parent effects retain their composition order.

Keyframes and ordered transforms are mutually exclusive. Ordered ASS operations preserve source order and content time, including negative start times and accel=0; negative accel receives an explicit loss diagnostic because its start evaluates to nonfinite geometry. POWER curves require a finite positive exponent. Advanced-source edits preserve unchanged mask identities and native animation. A changed geometric projection retains the original fixed pivot and rebases through its invertible transform; singular transforms and incompatible rectangle transforms on rotated/mirrored masks reject explicitly. Use the native mask panel for those edits.

Select the main subtitle Clip and open View → Masks. This panel docks or floats independently of Effects and has no expander. Activate the rectangle or Bézier icon: the tool highlights and Preview uses a crosshair. Drag to create a rectangle; a click with no area leaves the existing mask intact. Handles resize existing rectangles. For a free path, click nodes, drag their handles, and close the contour to commit it. The shared End editing shortcut (Escape by default, configurable) exits the tool. Invalid numeric fields retain their local Escape restore behavior.

Coordinates, scale, pivot, node positions and relative handles use the shared vector input. Select a contour and its point-list entry for node fields and timeline rows. Alt-drag a node to edit its outgoing handle; Alt+Shift-drag edits its incoming handle. Ctrl-click an existing node to delete it on release; clicking between nodes does not choose a nearby point. Capture loss, Escape, selection replacement and releasing outside the node cancel deletion. Unavailable toolbar actions are disabled; inversion uses an icon toggle. Clear node deformation to unlock topology while preserving overall transform animation. Deleting the last point removes its contour; deleting the last contour or clearing the mask removes all mask tracks in the same Undo transaction.

The toolbar aligns equal-size buttons in four groups separated by centered vertical rules. Groups remain intact when the panel narrows. Ending editing preserves the selected Clip's outline and gray shading of its excluded region, including inverse masks and nonzero contour holes; handles and the crosshair are active only while editing. All overlays clip to the video picture and cannot cover the transport or timeline. The gray shading is a canvas cue and is not included in composed preview pixels or exported video. Floating, hiding and reattaching the panel preserve authoritative numeric raw drafts.

Personal Dock layouts upgrade to v4 separately from project v5. Legacy v1/v2/v3 custom topology is retained, with Masks initially hidden. Standard and Effects built-in layouts place Masks beside Effects.

Native ASS rectangles retain supported transforms. Other mask animation expands at project frame rate; identical centisecond buckets merge. Vector coordinates quantize to 1/64 project pixels. Reports distinguish mask support from unsupported composition, fonts and shadow-blur semantics. Reimport produces separate static Clips. SRT reports mask/animation loss.

Automated results and reproducible artifacts are in [artifacts/verification/clip-mask](../../artifacts/verification/clip-mask). The [final validation report](../../artifacts/verification/clip-mask/validation-summary.md) records the latest affected test counts, Release build results, TRX files, Rider diagnostics, UI captures and project preservation evidence. Failed intermediate runs are retained in the execution evidence and excluded from final totals.

The real project was read through a copy and saved only to another copy. External subtitle/timing edits were observed during execution; the latest v4 contents were preserved and the verification source hash is recorded. The original and animated demo both round-trip canonically. The demo ASS contains 90 static sampled vector events plus the native rectangle and unmasked sibling, and reimports as 92 Clips.

The actual worker tests export software H.264 with CRF=0, decode three frames, and compare the target mask, moving subtitle, sibling and background with bounded YUV conversion tolerance. Offscreen F16 tests exercise HDR values; these tests do not establish native HDR display, Windows acceptance or hardware encoding. Headless hosts close after the actual input tests. Physical clipping edges, path-morph appearance and editing feel remain user acceptance items, using [mask-demo.aeginext](../../artifacts/verification/clip-mask/mask-demo.aeginext) and the exported previews.
