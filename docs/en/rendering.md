# Rendering integration

[English](rendering.md) · [简体中文](../zh-cn/rendering.md) · [All guides](README.md)

## Use the scene renderer

`src/AegiNext.Rendering` provides UI-independent shaping and scene rendering. Evaluate an immutable project, prepare its resources, and use `ProjectSceneRenderer` for preview/export. Each thread owns its renderer and disposable surfaces; Core remains free of graphics dependencies.

The scene supports subtitles, retained shapes/images/groups, keyframes, cubic paths, karaoke, Clip masks, blur, and extended blending. The workbench edits subtitles/masks; general shape/image/group tools are not exposed there.

## Pixel contract

| Field | Meaning |
|---|---|
| Storage | Tight top-down RGBA Half/F16 |
| Color | Extended linear BT.709/sRGB primaries; finite negative/>1 RGB allowed |
| Alpha | Premultiplied, 0–1; zero alpha requires zero RGB |
| White | Explicit `ReferenceWhiteNits`, default 203; channel 1 is reference white |
| Coordinates | Top-left origin, X right/Y down, target pixels |
| Composition | Linear source-over; surfaces must agree on reference white |

Copy pixels into caller-owned storage; do not expose borrowed native memory. Preview converts only the final composed image to SDR BGRA8. HDR export composites original frames with F16 overlays; display pixels never feed encoding.

## Text and editing geometry

`TextShaper` takes explicit font bytes, size, direction, and language. Glyph clusters are UTF-16 indices, not character counts. Its run API requires valid single-line/single-script text; callers perform segmentation, and mixed bidi/per-run fallback remain incomplete.

Scene rendering adds basic multiline/wrapping and grapheme karaoke. Actual glyph ink bounds determine geometry; stroke/shadow/blur do not change Pivot. Blank text uses a logical editing box. Anchor is canvas-relative, Pivot ink-relative, and Offset is pixels. `GetLayerGeometry` shares bounds and transforms with drawing, hit testing, and drag.

## Clip masks

Render the complete subtitle and its own blur first, then clip in project coordinates before parent composition. Masks affect only their Clip. Subtitle/parent transforms do not transform the mask; the mask's own fixed-pivot transform does. Preserve contour direction/nonzero winding and inversion. Preview cache identity includes evaluated mask geometry.

## Optional macOS HDR diagnostic

Build `-Target All`, then launch the matching desktop output with `--hdr-probe`. This separate diagnostic presents F16 through Media → Vulkan/MoltenVK/libplacebo → FP16 Linear Display P3/CAMetalLayer EDR. It is independent of the SDR workbench and video export; Windows HDR display is deferred.

The upload uses explicit reference-white normalization and current display headroom. Nominal 203 nits is an application scale, not measured screen brightness. Creation/presentation/destruction obey main-thread ownership; close awaits native destruction. Dependency locks are in `native/dependencies.json`.

## Verify

```powershell
dotnet test Tests/AegiNext.Rendering.Tests/AegiNext.Rendering.Tests.csproj -c Release
```

Tests load real Skia/HarfBuzz libraries and pinned [font fixtures](../../Tests/AegiNext.Rendering.Tests/Fixtures/README.md). Check pixels, alpha, geometry, and extended values. Offscreen readback and tags do not establish physical HDR appearance; display validation needs the target screen.
