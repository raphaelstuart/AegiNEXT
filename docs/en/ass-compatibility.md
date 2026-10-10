# ASS interoperability

[English](ass-compatibility.md) · [简体中文](../zh-cn/ass-compatibility.md) · [All guides](README.md)

AegiNext retains its native expressive range. Import and export convert meaningful counterparts and report losses without a compatibility switch or changes to the project.

See the [ASS tag audit](ass-tag-audit.md) for the four requested dimensions: native capability relationship, each conversion direction, implementation status, and remaining change size.

## Classification

**Corresponding** means both formats have the same concept; **superset** means the native model also supports values or combinations unavailable in ASS; **missing** means native editing has no equivalent yet.

**Implemented lossless conversion** below refers only to the stated parameter and timing subset, not byte-for-byte file round trips or identical pixels across renderers. ASS centisecond event times, integer millisecond transform times, 8-bit sRGB, font selection, and layout constrain round trips. Native rational times, linear HDR colors, and custom layout may require lossy export. The conversion report describes the actual file.

## Capability matrix

| Feature | Native relationship | Conversion status and limits |
|---|---|---|
| Text, event times, stacking | Superset | Implemented lossless for ordinary text, centisecond boundaries, and subtitle drawing order. Finer times are quantized with a report; native track identity has no ASS equivalent. |
| Style names, font, size, emphasis | Corresponding | Implemented lossless for representable attributes. Conflicting or unsafe names are renamed with a report. Font assets and named variants cannot be fully embedded in the exported ASS. |
| Explicit numeric font weight | Partial correspondence and gaps | Native named font variants already store Weight, but arbitrary ASS weights have no direct editor/conversion. Lossless parameter mapping to a matching variant is feasible but unimplemented; import currently reduces to normal/bold with a report. |
| Inline styles and `\r` | Corresponding | Implemented lossless for supported fields. Mixed inline geometry can retain text animation ranges, with pivot and layout limits. |
| Fill/stroke/shadow colors and alpha | Superset | Implemented lossless for the ASS 8-bit sRGB subset. Extended linear color, HDR, and additional precision cannot be exported losslessly. |
| Stroke width, shadow X/Y | Corresponding | Implemented lossless for representable static values. Nonuniform layer scaling changes the relationship between glyphs, strokes, and shadows. |
| Alignment, margins, explicit position | Superset | Implemented lossless for ordinary ASS alignment/margins/position. Independent text alignment, custom line height, and arbitrary pivots do not map generally. |
| Whole-line scale and Z rotation | Corresponding | Implemented lossless for supported static parameters. Mixed inline geometry retains text ranges sharing the subtitle anchor; shaping and compositing may differ. |
| `\move` | Superset | Implemented lossless for representable constant-speed straight segments. Varying speed is reported as an approximation; complex paths do not map losslessly. |
| `\fad` / `\fade` | Superset | Implemented lossless for supported envelope parameters. Complex native envelopes do not map generally; layer opacity and ASS internal fade produce different overlapping pixels. |
| `Spacing` / `\fsp` | Corresponding | Implemented lossless for representable static spacing, including inline overrides, resets, and constant animation. Shaping may still differ across engines. |
| Fill/stroke blur and `\blur` | Superset | Edge selection and blur-unit conversion implemented. Generally not lossless: ASS selects an edge based on stroke presence and couples shadow blur; independent blurs and different rasterizers cannot all be preserved. |
| `WrapStyle` / `\q` | Partial correspondence and gaps | Implemented lossless for no-wrap and explicit line-break semantics. Natural wrapping corresponds to q1, with font/algorithm differences. q0/q3 import as natural with a report; balanced wrapping is feasible but unimplemented. Grapheme export uses q1 with a report. |
| Common whole-line numeric `\t` | Superset | Implemented lossless for representable spacing, stroke width, edge blur, scale, and Z-rotation parameters with linear/power/ordered timing. Mixed inline animations and independent axes retain ranges and ordered component masks. Moving pivots and appearance coupling have specific limits; complex native curves are sampled with reports. |
| Color animation | Superset | ASS RGB and alpha retain independent masks, source order and sRGB interpolation metadata. Native linear colors use sampled export; 8-bit quantization and HDR limits remain lossy. |
| `\k`, `\kf`/`\K`, `\ko`, `\kt` | Superset | Complete multi-character groups, independent endpoints, gaps, overlaps, and reverse order are preserved. Representable centisecond timing and modes convert without parameter loss; necessary `\kt` carries a player compatibility report. State edges, style runs within a group, inactive caches, and untimed state appearance have specific loss reports. |
| Rectangle/vector clips | Superset | Implemented lossless for supported static geometry, inversion, and expressible rectangle animation. Other animation uses frame samples with rounding/sampling reports. |
| `\p` vector drawing | Partial correspondence and gaps | Native Bézier shapes exist. Lossless single-contour conversion is feasible but unimplemented; mixed text/drawing and multiple contours lack a complete counterpart. Drawing commands are not imported as ordinary text. |
| Opaque ASS background box | Missing | Corresponding rendering feasible, but complete box semantics are unimplemented and reported as omitted. |
| Pseudo-3D X/Y rotation, shear, `\be` | Missing | Unimplemented; these infrequent features are outside the current priority scope. |
| Native images, shapes, layer blur, HDR composition, scripts | Superset | Not generally convertible without loss. Complete projects and video export retain these capabilities. |

## Fidelity repairs in this revision

- Opening, previewing, editing, and saving imported multi-character groups no longer split them automatically. Explicit “Split into characters” or “Reset” redistributes time. Enabling highlighting also generates grapheme timing on first use when no timing records exist; existing records are retained or restored. Complete Unicode graphemes and original rational boundaries are retained. Legacy redistribution runs once while migrating v3–v10 projects to v11.
- Empty `\c` / `\1c`–`\4c` reset RGB while retaining channel alpha. Empty alpha resets opacity; empty `\xshad` and `\yshad` reset only their axis, and empty `\an` / `\a` use the current reset style's alignment.
- `ScaledBorderAndShadow: yes` uses target/PlayRes for border and shadow; `no` uses target/LayoutRes, with scale 1 and a report when LayoutRes is absent or incomplete. Shadow axes scale independently. Nonuniform border scaling uses a reported geometric-mean approximation, without additional changes to font, position, margins, or blur conversion.
- Each group's start and end are quantized independently. Export retains out-of-window clocks and all text instead of pushing a group after its predecessor. Ordinary cumulative tags remain in use when sufficient; overlaps, reverse order, and pre-event clocks use `\kt`. Collapsed positive durations retain at least one centisecond with a report; values outside the player's 32-bit millisecond clock are rejected.
- Karaoke, motion, fades, numeric transforms, and masks share the actual quantized event origin. Negative instantaneous visual transforms are retained. Zero-origin state changes avoid `\t(0,0,...)`, which libass interprets as a whole-event transition; unavailable hidden appearance is reported.

Native shadows originate from filled glyphs, while ASS shadows include outlines; zero-offset ASS shadows also differ from a native composited layer. These remain reported lossy conversions without changing native project rendering. Untimed/inactive state appearance and cached inactive timing also receive loss reports because ordinary ASS body text cannot store them.

## Priority

1. **P0: reliable conversion and actionable loss reports** — implemented naming, timing/number precision, shadow semantics, detailed review, and safe cancellation.
2. **P1: common geometry and opacity** — implemented static transforms, straight motion, and fades using native editors.
3. **P1: spacing, edge blur, wrapping** — native model, rendering, migration, animation/DSL, three UI entry points, and static ASS conversion implemented.
4. **P1: common whole-line numeric transforms** — implemented corresponding animations in both directions using native tracks/UI, with specific loss reports.
5. **P2: explicit weights, balanced wrapping, and vector drawing** — require separate design and validation while retaining the native model.

Attachments, dialogue Effect fields, legacy encodings, and Aegisub project metadata are not priorities.

## Animation boundaries

- Single continuous transforms import as power keyframes; overlapping, instantaneous, or zero-acceleration transforms use ordered operations. Mask transforms can coexist. Content offsets preserve phase; different visible runs retain independent base values and text ranges, including font size, shadow offsets, colors and channel alpha.
- Export preserves linear curves, power curves starting at phase zero, and expressible ordered operations. Native linear colors and complex curves are sampled, targeting output component error 1/255, minimum 1 ms intervals and at most 4096 samples per track; limits report possible excess error. Each sampled mask event rebases animation to its actual ASS start, and inline `\r` resets are followed by the animation again.
- Zero-scale entrances are supported. Native mirrored scale stays editable; negative scale is not forced into ordinary ASS scaling. Nonuniform scale, rotating shadows, and independent blurs have different operation order. Pivots requiring changing position cannot generally map to fixed `\pos`.
- Ordered tracks remain editable in the transform-operation editor; ordinary fields show evaluated values with a hint. Canvas position dragging remains available. Scale, rotation, and stroke-width inputs cover their native model ranges.

## Evidence

Tag semantics follow the [official Aegisub tag reference](https://aegisub.org/docs/latest/ass_tags/). Automated checks cover conversion parameters, clocks, evaluation, undo, persistence, rendering geometry, and UI. The optional [libass reference suite](../../Tests/AegiNext.Rendering.Tests/Reference/README.md) was also run against libass 0.17.5: 19 cases passed. The original one runtime/font check, seven strict timing/color/alpha comparisons, five known-loss characterizations and three oracle/negative controls are joined by one continuous RGB/alpha comparison and two absolute/relative font-size ink-growth comparisons. Font-size checks normalize each engine against its t0 ink size with a 3-pixel tolerance; different Skia/FreeType baseline font metrics prevent a pixel-lossless claim. These checks confirm the tested behavior and the reported shadow differences; they do not establish pixel-identical conversion for every tag combination.

Project v12 persists text ranges and complete animation targets. UTF-16 ranges follow full grapheme boundaries and remap identities/references during text edits, splitting, copying and project merges. Range-center pivots, overlapping geometry, continuous layout, independent state edges and blur routing across zero border remain reported limitations. Advanced ASS projection displays convertible style animation and range geometry; whole-layer position, scale, rotation and opacity use the native clip editor. An unchanged projection preserves native precision; explicit text rewrites replace only text-target tracks atomically and preserve whole-layer animation.
