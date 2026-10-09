# ASS interoperability

[English](ass-compatibility.md) · [简体中文](../zh-cn/ass-compatibility.md) · [All guides](README.md)

AegiNext retains its native expressive range. Import and export convert meaningful counterparts and report losses without a compatibility switch or changes to the project.

## Classification

**Corresponding** means both formats have the same concept; **superset** means the native model also supports values or combinations unavailable in ASS; **missing** means native editing has no equivalent yet.

**Implemented lossless conversion** below refers only to the stated parameter and timing subset, not byte-for-byte file round trips or identical pixels across renderers. ASS centisecond event times, integer millisecond transform times, 8-bit sRGB, font selection, and layout constrain round trips. Native rational times, linear HDR colors, and custom layout may require lossy export. The conversion report describes the actual file.

## Capability matrix

| Feature | Native relationship | Conversion status and limits |
|---|---|---|
| Text, event times, stacking | Superset | Implemented lossless for ordinary text, centisecond boundaries, and subtitle drawing order. Finer times are quantized with a report; native track identity has no ASS equivalent. |
| Style names, font, size, emphasis | Corresponding | Implemented lossless for representable attributes. Conflicting or unsafe names are renamed with a report. Font assets and named variants cannot be fully embedded in the exported ASS. |
| Explicit numeric font weight | Missing | Lossless conversion feasible but unimplemented. Currently reduced to normal/bold with a report. |
| Inline styles and `\r` | Corresponding | Implemented lossless for supported fields. Different transforms within one line do not map to a single native layer transform. |
| Fill/stroke/shadow colors and alpha | Superset | Implemented lossless for the ASS 8-bit sRGB subset. Extended linear color, HDR, and additional precision cannot be exported losslessly. |
| Stroke width, shadow X/Y | Corresponding | Implemented lossless for representable static values. Nonuniform layer scaling changes the relationship between glyphs, strokes, and shadows. |
| Alignment, margins, explicit position | Superset | Implemented lossless for ordinary ASS alignment/margins/position. Independent text alignment, custom line height, and arbitrary pivots do not map generally. |
| Whole-line scale and Z rotation | Corresponding | Implemented lossless for supported static parameters. Mixed inline geometry is unsupported; shaping and compositing may differ. |
| `\move` | Superset | Implemented lossless for representable constant-speed straight segments. Varying speed is reported as an approximation; complex paths do not map losslessly. |
| `\fad` / `\fade` | Superset | Implemented lossless for supported envelope parameters. Complex native envelopes do not map generally; layer opacity and ASS internal fade produce different overlapping pixels. |
| `Spacing` / `\fsp` | Corresponding | Implemented lossless for representable static spacing, including inline overrides, resets, and constant animation. Shaping may still differ across engines. |
| Fill/stroke blur and `\blur` | Superset | Edge selection and blur-unit conversion implemented. Generally not lossless: ASS selects an edge based on stroke presence and couples shadow blur; independent blurs and different rasterizers cannot all be preserved. |
| `WrapStyle` / `\q` | Partial correspondence and gaps | Implemented lossless for no-wrap and explicit line-break semantics. Natural wrapping corresponds to q1, with font/algorithm differences. q0/q3 import as natural with a report; balanced wrapping is feasible but unimplemented. Grapheme export uses q1 with a report. |
| Common whole-line numeric `\t` | Superset | Native keyframes, ordered transforms, easing, and UI exist; corresponding spacing/blur/stroke/scale/Z-rotation conversion is not yet connected. |
| Color animation | Superset | Generally not lossless: native interpolation uses linear RGBA, while ASS uses a different color space. Not currently imported as equivalent animation. |
| `\k`, `\kf`/`\K`, `\ko` | Superset | Implemented lossless for ordinary syllable timing and modes. Independent before/after appearance, sweeps, and centisecond rounding have reported limits. |
| Rectangle/vector clips | Superset | Implemented lossless for supported static geometry, inversion, and expressible rectangle animation. Other animation uses frame samples with rounding/sampling reports. |
| `\p` vector drawing | Partial correspondence and gaps | Native Bézier shapes exist. Lossless single-contour conversion is feasible but unimplemented; mixed text/drawing and multiple contours lack a complete counterpart. Drawing commands are not imported as ordinary text. |
| Opaque ASS background box | Missing | Corresponding rendering feasible, but complete box semantics are unimplemented and reported as omitted. |
| Pseudo-3D X/Y rotation, shear, `\be` | Missing | Unimplemented; these infrequent features are outside the current priority scope. |
| Native images, shapes, layer blur, HDR composition, scripts | Superset | Not generally convertible without loss. Complete projects and video export retain these capabilities. |

## Priority

1. **P0: reliable conversion and actionable loss reports** — implemented naming, timing/number precision, shadow semantics, detailed review, and safe cancellation.
2. **P1: common geometry and opacity** — implemented static transforms, straight motion, and fades using native editors.
3. **P1: spacing, edge blur, wrapping** — native model, rendering, migration, animation/DSL, three UI entry points, and static ASS conversion implemented.
4. **P1: common whole-line numeric transforms** — use native tracks and UI for corresponding animations and report remaining losses.
5. **P2: explicit weights, balanced wrapping, vector drawing, and color-animation policy** — require separate design and validation while retaining the native model.

Attachments, dialogue Effect fields, legacy encodings, and Aegisub project metadata are not priorities.

## Evidence

Tag semantics follow the [official Aegisub tag reference](https://aegisub.org/docs/latest/ass_tags/). Automated checks cover conversion parameters, clocks, evaluation, undo, persistence, rendering geometry, and UI. There is no independent libass pixel comparison, so pixel-identical conversion is not claimed.
