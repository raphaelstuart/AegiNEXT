# ASS tag audit

[English](ass-tag-audit.md) · [简体中文](../zh-cn/ass-tag-audit.md) · [All guides](README.md)

This table uses AegiNext's native editable capabilities as the benchmark against the [Aegisub ASS tag catalog](https://aegisub.org/docs/latest/ass_tags/). AegiNext does not aim to reproduce Aegisub's data model or renderer. See [ASS interoperability](ass-compatibility.md) for file-exchange boundaries.

## Classification

| Dimension | Meaning |
|---|---|
| Native relationship | **Superset**: a broader value domain, independent channels, or editing freedom; **Subset**: only part of the capability; **Not implemented**: no corresponding native semantics; **Same implementation**: the shared capability defined by this row corresponds |
| Conversion capability | **Lossless**: parameters, geometry, or timing survive under this row's conditions; **Lossy**: quantization, approximation, sampling, lost independent information, or differing composition; **Cannot convert**: no corresponding representation in the current model or target format |
| Implementation status | **Complete**: the described conversion and necessary loss handling exist; **Incomplete**: an existing model can carry it, but the adapter is unfinished; **Cannot complete**: direct tag correspondence needs a new native capability or a different output form |
| Change size | **Small**: local adapter or diagnostic; **Medium**: coordination across parsing, clocks, measurement, or conversion; **High**: new layout/rendering capabilities or event baking. Small completed items describe maintenance size |

Capability and implementation are separate: a lossless but incomplete adapter is not available yet. A lossy but complete adapter has an implemented fallback that still loses information. Cannot complete is limited to the current model, rather than a permanent claim about future extensions.

Lossless does not include original tag spelling or order, operation identity, source-file metadata, or pixel equivalence. Shared parameters must satisfy native validation and output precision: at most nine decimal places, centisecond dialogue/karaoke time, integer millisecond transforms, and 8-bit color/alpha. Actual quantization makes a conversion lossy and produces diagnostics. Different capabilities of a tag occupy separate rows; no single row covers all its uses.

Per-operation numeric constraints are checked against libass 0.17.5 reference semantics. The blur cap of 100 belongs to that reference implementation; other ASS players can use different limits.

## Fonts, text, and layout

| Tag or capability scope | Native relationship | ASS→native | Native→ASS | Import / export status | Size | Boundary |
|---|---|---|---|---|---|---|
| `\fn` family; positive static `\fs`; `\b0/1`, `\i`, `\u`, `\s` | Same implementation | Lossless | Lossless | Complete / Complete | Small | Base and inline overrides; the same font environment is required, and metrics/rasterization can differ |
| `\b100`–`\b900` and native named font variants | Superset | Lossy | Lossy | Incomplete / Incomplete | Medium | Native variants store Weight, Width, and identity; the ASS path reduces weight to regular/bold, with diagnostics, and lacks a complete weight bridge |
| Font-resource identity and named variants | Superset | Lossy | Lossy | Incomplete / Complete | Medium | ASS attachments are not imported; export writes family/emphasis and reports resource or variant loss |
| Static `\fs0`, relative `\fs+/-`, and nonpositive-result resets | Same implementation | Lossless | Lossless | Complete / Complete | Small | Final sizes must remain within native limits; static nonpositive results restore the reset style, while dynamic resets belong to the lossy constraint row below |
| Static and inline `\fsp` spacing | Same implementation | Lossless | Lossless | Complete / Complete | Small | Negative spacing is supported; shaping clusters and ligatures can still differ |
| `\r`, `\rStyle`, and empty-parameter resets for supported properties | Same implementation | Lossless | Lossless | Complete / Complete | Small | Resets style without clearing the independent karaoke clock; unknown styles produce a fallback diagnostic |
| `\N`, `\h`, and `\n` text semantics under the current q mode | Same implementation | Lossless | Lossless | Complete / Complete | Small | Import converts soft breaks to hard breaks under q2 and otherwise to spaces; source escape spelling is not retained |
| `\q1` natural wrapping and `\q2` no automatic wrapping | Same implementation | Lossless | Lossless | Complete / Complete | Small | Modes survive, but Unicode breaking and font metrics can change actual line boundaries |
| `\q0/3` balanced smart wrapping | Not implemented | Lossy | Cannot convert | Complete / Cannot complete | High | Import uses natural wrapping with a diagnostic; native layout lacks the top/bottom balance strategies |
| Native grapheme wrapping, custom line height, and independent text alignment | Superset | Cannot convert | Lossy | Cannot complete / Complete | High | Export uses q1, default line height, and alignment derived from nine-way placement, with separate diagnostics |
| `\an` (1–9), legacy `\a`, and three margins | Same implementation | Lossless | Lossless | Complete / Complete | Small | Left/right/vertical margins map separately; zero dialogue margins inherit style values |
| `\fe` font encoding and base-direction control | Not implemented | Cannot convert | Cannot convert | Cannot complete / Cannot complete | Medium | Recognized with an unsupported-tag diagnostic; native Unicode text has no corresponding encoding state |

## Color, borders, shadows, and blur

| Tag or capability scope | Native relationship | ASS→native | Native→ASS | Import / export status | Size | Boundary |
|---|---|---|---|---|---|---|
| `\c`, `\1c/2c/3c/4c`, `\alpha`, `\1a/2a/3a/4a` in the shared 8-bit domain | Same implementation | Lossless | Lossless | Complete / Complete | Small | Secondary color maps to inactive fill on timed text; RGB and alpha can be edited/reset separately |
| Native floating-point linear color, HDR, and negative RGB | Superset | Cannot convert | Lossy | Cannot complete / Complete | Small | ASS cannot carry the same precision/domain; export uses 8-bit sRGB with quantization or clipping diagnostics |
| Uniform isotropic `\bord` | Same implementation | Lossless | Lossless | Complete / Complete | Small | Negative source values become zero; geometric-mean compensation for nonuniform resampling is lossy and diagnosed |
| Equal-width `\xbord/ybord` alias pair | Same implementation | Lossless | Lossless | Incomplete / Complete | Small | Import lacks the alias adapter; native equal widths can export as bord |
| Truly independent `\xbord/ybord` widths | Not implemented | Cannot convert | Cannot convert | Cannot complete / Cannot complete | Medium | Native styles have one StrokeWidth and do not currently approximate independent axes |
| Static and directly representable `\shad` / `\xshad/yshad` offset animation | Same implementation | Lossless | Lossless | Complete / Complete | Small | Animation must need neither per-operation clamping nor coordinate coupling; shad is nonnegative and x/yshad are signed, with sampled cases in the constraint row below |
| ASS shadow contours including borders, and zero-offset/no-blur composition | Subset | Lossy | Lossy | Complete / Complete | High | Native shadows use fill glyphs; ShadowComposition diagnoses the difference without reproducing ASS composition |
| Coupled `\blur` with a fixed border category | Superset | Lossy | Lossy | Complete / Complete | Small | Native fill/stroke/shadow blur are independent; compatible amounts convert to Gaussian sigma with kernel/composition differences |
| Negative/over-100 `\blur` and continuous source operations | Superset | Lossy | Lossy | Complete / Complete | Small | External source operations clamp after interpolation to 0–100; export keeps raw targets above the cap and diagnoses playback limits |
| Independent blur channels and blur animation across bord zero | Superset | Lossy | Lossy | Complete / Complete | High | Selects the visible channel; changing targets or incompatible independent channels are omitted with diagnostics |
| Native layer blur | Superset | Cannot convert | Cannot convert | Cannot complete / Cannot complete | High | ASS edge blur has no equivalent tag at the post-composition stage; export reports omission |
| Repeated box edge blur `\be` | Not implemented | Cannot convert | Cannot convert | Cannot complete / Cannot complete | Medium | Native Gaussian blur has no equivalent repeated-box operator; currently reported as unsupported |

## Placement, transforms, and animation

The current `\t` property whitelist is fs, fsp, bord, blur, fscx/fscy, fr/frz, shad/xshad/yshad, c/1c–4c, alpha/1a–4a, and rectangular clip/iclip. Recognizing a tag name does not mean it can animate inside a transform; other nested properties produce unsupported-tag diagnostics.

| Tag or capability scope | Native relationship | ASS→native | Native→ASS | Import / export status | Size | Boundary |
|---|---|---|---|---|---|---|
| Static `\pos` placement | Same implementation | Lossless | Lossless | Complete / Complete | Small | Exporting custom pivots requires real font measurement; missing measurement uses a lossy placement estimate |
| Single constant-speed straight `\move` | Same implementation | Lossless | Lossless | Complete / Complete | Small | Source times use integer milliseconds; reversed or instantaneous motion is skipped with a diagnostic |
| Native monotonic collinear motion with variable speed, or a single straight path | Superset | Cannot convert | Lossy | Cannot complete / Complete | Medium | ASS move lacks the same speed curve; export preserves endpoints/duration with a constant-speed approximation |
| Curved, returning, multisegment motion and path progress/orientation | Superset | Cannot convert | Lossy | Cannot complete / Incomplete | High | Full event baking is unfinished; current export omits incompatible motion, retains static placement, and reports loss |
| Nonnegative `\fscx/fscy` 2D scale and `\fr/frz` Z rotation | Same implementation | Lossy | Lossy | Complete / Complete | Small | Native transforms occur after layout; ASS glyph scaling participates in layout, so run placement and shared layout can differ |
| Native signed mirror scale and mirrored animation stages | Superset | Cannot convert | Lossy | Cannot complete / Incomplete | High | Negative source fsc clamps to zero; export currently omits negative axes with diagnostics and does not bake mirroring through 3D/drawing |
| Independent range pivots and overlapping composite range geometry | Superset | Lossy | Lossy | Complete / Complete | High | Source runs map to ranges; export retains representable components and diagnoses incompatible pivots or stacking |
| Fixed 2D rotation origin `\org` | Same implementation | Lossless | Lossless | Incomplete / Incomplete | Medium | Native Pivot exists, but org is neither imported nor emitted; fixed-layout geometry needs coordinate and measurement adaptation |
| `\frx/fry` 3D rotation and perspective | Not implemented | Cannot convert | Cannot convert | Cannot complete / Cannot complete | High | Native 2D transforms lack these degrees of freedom |
| Glyph shear `\fax/fay` | Not implemented | Cannot convert | Cannot convert | Cannot complete / Cannot complete | High | LayerTransform has neither shear nor a full affine matrix |
| Supported-property `\t` power curves, ordered overlaps, and instantaneous operations | Same implementation | Lossless | Lossless | Complete / Complete | Small | Limited to directly representable numeric operations or sRGB RGB/alpha masks; source order survives |
| Per-operation `\t` constraints, font resets, or independent shadow axes under rotation | Subset | Lossy | Lossy | Complete / Complete | Medium | Native tracks lack the same source-constraint operations; evaluates source coordinates before conversion, sampling as needed with floor/discontinuity diagnostics |
| Native linear RGBA, partial RGB masks, multiplication, and arbitrary text curves | Superset | Cannot convert | Lossy | Cannot complete / Complete | Medium | Supported evaluable text properties sample into ASS; native curve and operation identities are not retained |
| Dynamic layout through `\t(\fs/\fsp)` | Superset | Lossy | Lossy | Complete / Complete | Medium | Values correspond, but native per-frame layout differs from ASS; layout/sampling diagnostics apply |
| Valid `\fad/fade` envelopes and channel composition | Same implementation | Lossy | Lossy | Complete / Complete | Small | Constants or at most two transitions correspond; native layer opacity and internal ASS fades differ on overlapping pixels |
| Native eased opacity envelopes | Superset | Cannot convert | Lossy | Cannot complete / Complete | Medium | Recognized envelopes export as linear approximations with diagnostics |
| More than two changes or overlapping ordered opacity | Superset | Cannot convert | Lossy | Cannot complete / Incomplete | Medium | Full alpha/event evaluation is unfinished; current export omits only the animation and reports loss |

## Karaoke, clips, and drawing

| Tag or capability scope | Native relationship | ASS→native | Native→ASS | Import / export status | Size | Boundary |
|---|---|---|---|---|---|---|
| Basic complete-group timing/fill `\k`, `\kf/\K`, `\ko` | Same implementation | Lossless | Lossless | Complete / Complete | Small | Shared colors, exactly representable centisecond endpoints without endpoint collapse, and no within-group style splits; no forced per-grapheme timing |
| Zero-duration groups `\k0`, `\kf0`, `\ko0` | Subset | Lossy | Cannot convert | Complete / Cannot complete | Medium | Native groups require positive duration; imported zero-duration text becomes ordinary text with a diagnostic |
| Independent `\kt` starts, gaps, overlaps, and reversed order | Superset | Lossless | Lossless | Complete / Complete | Small | Native endpoints stay independent; negative relative times rebase the content origin, and export diagnoses kt tool compatibility |
| Within-group range styles and shared-glyph sweep | Superset | Lossy | Lossy | Complete / Complete | Medium | Complete timing and range appearance survive, but player run activation and sweep advances can differ |
| Independent NORMAL/ACTIVE/INACTIVE fill and animation | Superset | Lossy | Lossy | Complete / Complete | Small | Normal fill inherited by timed inactive text emits 2c; explicit inactive overrides take priority, and active emits 1c; identity/precision do not fully round-trip |
| Independent state borders/shadows, continuous state tracks, and disabled caches | Superset | Lossy | Lossy | Complete / Complete | High | Representable activation instants export; sweeps/shared edges lose independent differences, and untimed parts are omitted with diagnostics |
| Static integer rectangle `\clip/iclip` | Same implementation | Lossless | Lossless | Complete / Complete | Small | Clips the owning subtitle and supports inversion; noninteger output coordinates quantize with a diagnostic |
| Static vector clip: closed m/l/b and supported s/p/c sequences | Same implementation | Lossless | Lossless | Complete / Complete | Small | Splines become Bézier curves; lossless coordinates require a 1/64-pixel grid on export, with no command/node identity retention |
| Permissive p/s/c combinations normalizable to closed Bézier geometry | Same implementation | Lossless | Lossless | Incomplete / Complete | Medium | Native models carry the shared geometry, but parsing only handles p/c adjacent to s and can reject other sequences; output must meet the coordinate precision conditions above |
| Same-mode rectangle `\t(\clip/iclip)` animation | Same implementation | Lossless | Lossless | Complete / Complete | Small | Matching timing and representable curves convert directly; mixed geometry/inversion modes or negative acceleration cause diagnosed omissions |
| Native vector nodes, handles, mask transforms, and arbitrary curves | Superset | Cannot convert | Lossy | Cannot complete / Complete | Medium | Expands at project frame rate into static events; centiseconds, coordinates, and reimported independent clips lose track structure |
| Single closed m/l/b `\p` drawing and native path shapes | Same implementation | Lossless | Lossless | Incomplete / Incomplete | Medium | Shared geometry exists, but drawing content is not imported and shape layers are not exported |
| Multiple independent closed contours without holes through `\p` | Same implementation | Lossy | Lossy | Incomplete / Incomplete | High | Can split into native shape layers, changing structure and overlapping composition; current bridging is unfinished |
| Compound contours with holes in one `\p` shape, and inline drawing layout | Subset | Cannot convert | Cannot convert | Cannot complete / Cannot complete | High | Native shape geometry has one path and no inline drawing text flow; full bridging needs new native capabilities |
| Static drawing baseline offset `\pbo` | Same implementation | Lossless | Lossless | Incomplete / Incomplete | Medium | Native position can carry the shared geometry; missing drawing adaptation blocks conversion |
| Native images and blended composition | Superset | Cannot convert | Cannot convert | Cannot complete / Cannot complete | High | Single ASS tags cannot express equivalent image resources or composition; export reports Composition loss |

File fields have separate limits: nonempty Effect fields and BorderStyle 3 background boxes are not imported; font/image attachments are not loaded; original comments, script metadata, and tag spelling do not fully round-trip. Native literal `\\` followed by control characters such as N/n/h can be rejected when safe output is impossible. Literal braces use libass extension escaping with a cross-player diagnostic. These limits preclude lossless source-file preservation.

## Implementation and verification boundaries

| Content | Implementation |
|---|---|
| File structure, styles, resolution, and overall loss | [AssSubtitleFormat](../../src/AegiNext.Application/SubtitleFormats/AssSubtitleFormat.cs) |
| Text, resets, and karaoke | [AssTextParser](../../src/AegiNext.Application/SubtitleFormats/AssTextParser.cs), [AssTextWriter](../../src/AegiNext.Application/SubtitleFormats/AssTextWriter.cs) |
| Text animation, projection preservation, and source constraints | [Import](../../src/AegiNext.Application/SubtitleFormats/AssTextAnimationImport.cs), [export](../../src/AegiNext.Application/SubtitleFormats/AssTextAnimationExport.cs), [projection restoration](../../src/AegiNext.Application/SubtitleFormats/AssTextAnimationProjection.cs), [source evaluator](../../src/AegiNext.Application/SubtitleFormats/AssSourceAnimationEvaluator.cs) |
| Placement, motion, opacity, and appearance compensation | [AssEventConversionContext](../../src/AegiNext.Application/SubtitleFormats/AssEventConversionContext.cs) |
| Clip geometry and event expansion | [AssMaskDrawing](../../src/AegiNext.Application/SubtitleFormats/AssMaskDrawing.cs), [AssMaskWriter](../../src/AegiNext.Application/SubtitleFormats/AssMaskWriter.cs), [AssMaskSampling](../../src/AegiNext.Application/SubtitleFormats/AssMaskSampling.cs) |
| Actual output precision, shadow, and blur semantics | [AssExportPrecision](../../src/AegiNext.Application/SubtitleFormats/AssExportPrecision.cs), [AssShadowComposition](../../src/AegiNext.Application/SubtitleFormats/AssShadowComposition.cs), [AssBlurConversion](../../src/AegiNext.Application/SubtitleFormats/AssBlurConversion.cs) |
| Native degrees of freedom | [SubtitleStyle](../../src/AegiNext.Core/Projects/SubtitleStyle.cs), [LayerTransform](../../src/AegiNext.Core/Projects/LayerTransform.cs), [AnimationProperty](../../src/AegiNext.Core/Projects/AnimationProperty.cs) |

The tag catalog defines the feature scope. Parsing boundaries also refer to the [versioned libass parser](https://github.com/libass/libass/blob/0.17.5/libass/ass_parse.c) and [drawing state machine](https://github.com/libass/libass/blob/0.17.5/libass/ass_drawing.c). Other players and Aegisub tools can support different extensions, especially kt. See the [reference-test notes](../../Tests/AegiNext.Rendering.Tests/Reference/README.md) for independent rendering methods and known-loss characterization. Passing tests does not establish lossless conversion for every ASS combination.
