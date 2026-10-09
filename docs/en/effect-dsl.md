# Effect scripts

[English](effect-dsl.md) · [简体中文](../zh-cn/effect-dsl.md) · [All guides](README.md)

## Apply and edit

1. Select a subtitle, choose a preset in **Effects**, and click **Apply Preset**.
2. Open **Settings → Effect Scripts** to manage templates. Builtins are read-only; **Save As** creates a personal copy.
3. Edit, **Validate**, then save or export UTF-8 `.aegifx`. Validation reports line/column errors; **Locate Error** moves the caret.

Completion opens while typing or with Cmd/Ctrl + Space. Arrows select, Enter/Tab insert, and Esc dismisses. Templates are personal settings; applying recompiles for the current Clip and creates one Undo transaction.

## Combining presets

Apply presets successively to combine them. Each preset replaces only the intervals in which it explicitly declares a complete animation target. Undeclared intervals preserve existing animation and its interpolation curves. With no existing target track, empty intervals retain the script's base or preceding value. Segment names such as `stay` and `hold` have no special meaning, and a `flex` segment may contain animation.

For a 4 s Clip, applying builtin fade-in followed by fade-out preserves the first 300 ms entrance and the middle animation, then adds the last 300 ms exit. Reversing the order produces the same result. A batch application creates one Undo transaction. Composition also works after saving and reopening the project.

An empty stay declares no properties:

```text
segment stay flex 1
end
```

This stay explicitly fixes opacity at its base value, replacing existing opacity animation:

```text
segment stay flex 1
    at 0 opacity base hold
    at 1 opacity base
end
```

`hold` is keyframe interpolation, not a request to skip an interval. `base` always reads the static layer property or subtitle style, not the old animation value at the join. Values must agree where new animation meets retained animation; otherwise the entire application fails without changing the project or Undo/Redo. Adjust the preset endpoints or clear the corresponding track first.

Independent presets allocate their timing separately and do not jointly compress fixed segments. Two 300 ms transitions join on a 600 ms Clip. On a 400 ms Clip they overlap, and builtin fade-in followed by fade-out fails because the join values differ. On Clips of 300 ms or less, each preset covers the entire Clip, so the last preset replaces the first. To retain both transitions with joint compression on short Clips, use one preset containing both segments, such as builtin fade-in-out.

## A first script

```text
effect "fade-in-out" version 1
short-clip compress

segment enter fixed 300ms
    at 0 opacity 0 ease-out
    at 1 opacity base
end

segment stay flex 1
    at 0 opacity base hold
    at 1 opacity base
end

segment exit fixed 300ms
    at 0 opacity base ease-in
    at 1 opacity 0
end
```

`fixed` preserves entrance/exit duration; `flex` divides the remaining time by weight. At least one flexible segment is required. Positions run from 0 to 1 inside each segment; each declared property must cover both endpoints in increasing order.

`short-clip compress` shrinks fixed segments proportionally when needed; `reject` refuses insufficient duration. For this script, a 5 s Clip holds for 4.4 s; a 400 ms Clip compresses to 200 ms in and 200 ms out. Shared endpoints must agree, including a zero-duration flexible segment.

## Properties and values

| Property | Value |
|---|---|
| `position` | Pixel vector: `base`, `(100, 20)`, `offset(-250, 0)` |
| `scale` | Vector: `base`, `(1, 1)`, `factor(0.2, 0.2)` |
| `rotation` | Degrees: `0`, `base`, `offset(15)` |
| `opacity` | 0–1 or `base` |
| `fill`, `stroke` | Linear `rgba(r, g, b, a)` or `base` |
| `blur` | 0–512 pixels or `base` |
| `letter-spacing` | Subtitle grapheme spacing, −4096–4096 pixels or `base` |
| `fill-blur`, `stroke-blur` | Subtitle fill / outline blur, 0–512 pixels or `base` |
| `path-progress` | Explicit 0–1 |
| `mask-rectangle-top-left`, `mask-rectangle-bottom-right` | Project-coordinate corner vectors |
| `mask-position`, `mask-scale`, `mask-rotation` | Independent mask transform |
| `mask-node(c,n).position`, `.in-handle`, `.out-handle` | Existing node position / relative handle vectors |

`base` reads the original target value; `offset` adds and `factor` multiplies it. Colors use straight linear RGB, permitting HDR values, with alpha 0–1; UI HEX is sRGB. `#` starts a comment, so HEX literals are not script values.

`letter-spacing`, `fill-blur`, and `stroke-blur` require a subtitle Clip and its original subtitle style, including when using explicit numbers. Their `base` values come from that style. The existing `blur` applies to the composited layer; the two channel blurs affect fill and outline separately. Wrap mode is a static subtitle style setting and is not an animated DSL property. These properties remain part of DSL version 1.

Easing is `hold`, `linear`, `ease-in`, `ease-out`, `ease-in-out`, or `power(positiveExponent)`. A point controls interpolation to the next point; default is linear.

## Masks and validation

Create mask geometry before applying mask scripts. Node selectors are one-based and resolve to stable IDs. Node/handle animation locks topology; clear those tracks before adding/removing/reordering nodes. Scripts combine declared intervals for matching complete targets, preserve other tracks, and never create geometry. Clear an ordered ASS transform target before replacing it with script keyframes.

IDs/segment names use lowercase ASCII letters, digits, `.` and `-`, begin with a letter, and have at most 64 characters. Limits: 128 segments, 4,096 points, 262,144 characters, fixed durations up to 24 h, and flex weights in (0, 1,000]. Invalid input or conflicting endpoints leave the project and Undo unchanged.

## Examples

- [Builtin fade](../../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx)
- [Slide/pop](examples/effects/slide-pop.aegifx)
- [Linear color cycle](examples/effects/color-cycle.aegifx)
- [Mask slide](examples/effects/mask-slide.aegifx)
- [Mask morph](examples/effects/mask-morph.aegifx)

Parser/compiler: `src/AegiNext.Core/Effects/`. For assisted authoring, use the repository [effect DSL skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md).
