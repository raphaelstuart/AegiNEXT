# Offscreen rendering contracts

[English](rendering.md) | [简体中文](zh-CN/rendering.md)

AegiNext.Rendering is the UI-independent Skia/HarfBuzz adapter. Its foundation uses CPU raster surfaces and verifies high-precision text/shape numbers; GPU interop/display mapping/video decode/encoding are separate modules. ProjectSceneRenderer subsequently adds Core scene evaluation/rendering on this foundation.

## Color, pixels, coordinates

| Item | Contract |
|---|---|
| Working space | Extended linear sRGB; negative/above-one RGB allowed. |
| Brightness | Explicit RenderSurfaceInfo.ReferenceWhiteNits; neutral 1 is reference white. Tests use 203 nits, API does not require it. |
| Color | Straight LinearColor, finite RGB [-65504,65504], alpha [0,1]; default transparent black. |
| Encoded input | FromSrgb accepts [0,1] encoded RGB, decodes transfer, preserves alpha. |
| Storage | Unnormalized premultiplied RgbaF16, not clamped F16/8-bit SKColor; HDR RGB may exceed alpha. |
| Coordinates | Top-left, X right/Y down, target pixels; text origin is baseline. All geometry finite. |
| Composition | Linear source-over; layer opacity covers entire source. Unequal surface reference whites reject. |
| Output | CopyPixels(Span<Half>) copies owned tight top-down per-pixel RGBA; byte/stride counts checked at description creation. |

Skia paint gets straight float color plus explicit linear color space, not untagged ColorF. Surface has explicit tight stride. Readback copies raw Half without unpremultiplying/tone mapping/lending native memory. [SKPaint](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKPaint.cs), [SKPixmap](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKPixmap.cs).

F16 rounding/tiny underflow use explicit test tolerances; extremes are separate. Display/encoding must interpret primaries/transfer/white/alpha, never guess from Half bytes.

## Shaping and scene geometry

TextShaper copies caller font bytes/face index, with no system lookup/silent fallback. Missing glyph/corrupt font/invalid text/unrepresentable size throws.

Shape accepts nonempty single-line/single-script UTF-16 run, pixel size, explicit LTR/RTL and language; HarfBuzz infers script. Utf16Cluster is a code-unit index, not character count; one cluster can have many characters/glyphs, surrogate pairs have two units, GlyphId is font index. Control/newline/unpaired surrogate input rejects before native shaping.

Run-level API does not implement bidi paragraph order/script splitting/line breaking/fallback/rich text/vertical layout. Segment runs before using it; don't assume mixed bidi is automatically laid out. [HarfBuzz boundaries](https://harfbuzz.github.io/what-harfbuzz-doesnt-do.html), [SKShaper buffer](https://github.com/mono/SkiaSharp/blob/v3.119.4/source/SkiaSharp.HarfBuzz/SkiaSharp.HarfBuzz/SKShaper.cs).

ProjectSceneRenderer adds basic multiline/wrapping, paragraph fallback, Unicode-grapheme karaoke, keyframes/cubic paths/subtitles/retained shapes/images/groups/animated Clip masks/extended blending/F16 blur. The workbench edits Clip masks; general shape/image/group tools remain outside the subtitle editor. Mixed bidi/per-run fallback remain incomplete. Preview/worker share validated prepared scenes and renderer-lifetime caches; each thread owns its renderer.

`SceneEvaluator` evaluates rectangle boundaries, mask transforms and stable-ID node/handle tracks into `EvaluatedLayer.Mask` using the subtitle's content clock. `ClipMask` applies only to its subtitle Clip. The renderer first draws the complete subtitle, including stroke, shadow, and karaoke, and applies the Clip's own blur on an F16 surface; it then clips in project coordinates before composition into the parent. Subtitle and parent transforms do not transform the mask, while the mask's own fixed-pivot transform does. Vector masks preserve contour direction with nonzero winding fill; inversion keeps the project-picture area outside the filled mask. Parent effects retain their existing order, and the clip does not crop sibling layers. Preview scaling projects the same geometry into the render surface; preview and video export share this path. Preview equivalence compares evaluated mask geometry so same-time edits and mask-only animation invalidate cached images.

InkBounds unions actual glyph bounds/positions rather than advance/blob estimates. GetLayerGeometry shares local bounds/pivot/base position/matrix with drawing/editing. Subtitle Anchor/Pivot/Offset use canvas/ink/pixels; scale/rotation pivot on ink. Stroke/shadow/blur do not change pivot; blank text gets HasInk=false logical bounds.

ComposePreview(output size) fits background/overlays proportionally with black letterboxing. Desktop preserves project aspect, and uses associated uncomposed background for editing to avoid double subtitles. The original overload retains background-size behavior. HDR export uses project F16 directly.

F16 intermediates retain extended-linear highlights/premultiplied alpha, avoiding Skia SDR paths. Final sRGB/BGRA8 preview is never export source. Highlight appearance shares original shaping and replaces base appearance only in the highlighted region, retaining geometry.

## Ownership and example

LinearRenderSurface/TextShaper are single-threaded/disposable. ShapedTextRun retains drawing resources after shaper disposal; after its own Dispose only managed metadata remains readable, drawing throws ObjectDisposedException. Composition synchronously copies source; later source clear/dispose cannot alter destination.

```csharp
using var shaper = new TextShaper(fontBytes);
using var text = shaper.Shape("AegiNext ffi", 48, TextDirection.LEFT_TO_RIGHT, "en");
using var layer = new LinearRenderSurface(new(1920, 1080, 203));
layer.DrawText(text, new(64, 980), new(2, 2, 2, 1));
var pixels = new Half[layer.Info.ChannelCount];
layer.CopyPixels(pixels);
```

This produces text at twice reference-white linear values in offscreen pixels; it does not prove HDR reached a display.

## Reproducibility

Tests/AegiNext.Rendering.Tests actually loads native libraries. [Font fixtures](../Tests/AegiNext.Rendering.Tests/Fixtures/README.md) have pinned hashes/official sources/OFL and require no installed fonts.

SkiaSharp/bridge lock 3.119.4; HarfBuzzSharp managed and all participating native packages lock 8.3.1.5. Central transitive pins align Desktop dependencies to avoid mixed versions in one process; Core gets no graphics dependency. Offscreen/native-window scopes are separately [recorded](README.md#implementation-evidence).
