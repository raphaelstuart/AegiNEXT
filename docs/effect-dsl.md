# Subtitle effect DSL v1

[English](effect-dsl.md) | [简体中文](zh-CN/effect-dsl.md)

This guide matches `src/AegiNext.Core/Effects/EffectScriptParser.cs` and `EffectScriptCompiler.cs`. Builtin source files are embedded in Core and read directly when applying presets: examples and execution share the source.

Implemented pieces include parsing, rational time allocation, full vector/color animation, transactions, seven builtins, settings libraries, and `.aegifx` interchange. The Effects selector combines builtin/personal scripts. Application compiles for the target clip instead of reusing another duration's keyframe snapshot.

## Editor and personal templates

Settings → Effect Scripts can Save As a builtin or create an empty template. Builtins are read-only; copies get a new stable ID. Personal names do not localize. Name/source drafts survive selection/language changes. Save validates the whole personal library and atomically writes `effect-scripts.json` in personal application data, independently of project/layout storage.

The editor uses native TextBox/TextPresenter layout for syntax colors: instructions, properties, functions, numbers, strings, easing, comments. Actual text input triggers contextual completion; Ctrl/Cmd+Space opens it explicitly. The caret-anchored popup does not reserve bottom code space or take focus. Arrows choose, Enter/Tab insert, Esc closes; pointer insertion also works. IME preedit, blur, or read-only templates close it. Native text selection/caret/IME behavior remains. There is no persistent bottom shortcut hint. Validate returns actual line/column errors; Locate Error moves the caret explicitly. Errors never steal focus or overwrite source.

Import/export is UTF-8 `.aegifx`; import validates grammar, budgets, and stable-ID conflicts against builtin/personal sources. Failure preserves the library. Builtins can export, but their IDs remain reserved: Save As or change effect ID before reimport. Settings manages scripts only; choose and Apply Preset in the workbench.

Use `$aeginext-effect-dsl` to author scripts with the project-specific [skill](../.agents/skills/aeginext-effect-dsl/SKILL.md).

## Source examples

- [Fade in/out](../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx): fixed entrance, flexible hold, fixed exit.
- [Fill/stroke color cycle](examples/effects/color-cycle.aegifx): full linear RGBA and fixed/flexible segments.
- [Slide/pop/fade](examples/effects/slide-pop.aegifx): full vectors, multiple properties/points per segment.
- [Mask slide](examples/effects/mask-slide.aegifx): independently translate an existing rectangle or vector mask.
- [Mask morph](examples/effects/mask-morph.aegifx): animate one existing node and its relative outgoing handle, with duration compression.
- [Fade in](../src/AegiNext.Core/Effects/Scripts/fade-in.aegifx), [fade out](../src/AegiNext.Core/Effects/Scripts/fade-out.aegifx), [pop in](../src/AegiNext.Core/Effects/Scripts/pop-in.aegifx), [pop out](../src/AegiNext.Core/Effects/Scripts/pop-out.aegifx), [slide in](../src/AegiNext.Core/Effects/Scripts/slide-in.aegifx), [slide out](../src/AegiNext.Core/Effects/Scripts/slide-out.aegifx).

Files are UTF-8 text; `#` starts a comment.

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

## Duration and allocation

`fixed 300ms` normally retains 300 ms. Durations use `s`/`ms`, up to six decimals, kept rational without video-frame/millisecond quantization.

`flex 1` shares remaining duration by weight. Weights 1/3 receive 25%/75%. At least one flexible segment is required, and may be empty; an empty segment holds prior properties until the next segment.

`at 0` / `at 1` mark segment endpoints; `at 0.7` is 70%. Positions allow six decimals. Each property in a segment must begin at 0, end at 1, and increase strictly. Properties may interleave.

| Clip duration | Entrance | Hold | Exit |
|---|---:|---:|---:|
| 5 s | 300 ms | 4.4 s | 300 ms |
| 1 s | 300 ms | 400 ms | 300 ms |
| 600 ms | 300 ms | 0 ms | 300 ms |
| 400 ms, compress | 200 ms | 0 ms | 200 ms |
| 400 ms, reject | Application rejected | Project preserved | Undo preserved |

Short-clip policy is explicit per script:

- `short-clip compress`: proportionally compress fixed segments when their total exceeds the clip; flexible duration becomes zero. Builtins use this.
- `short-clip reject`: refuse insufficient duration with a diagnostic.

All points of a zero-duration flexible segment coincide and must have equal values; varying values report endpoint conflicts rather than silently dropping animation. Adjacent shared endpoints for the same property must match; the merged point uses right-segment interpolation. Undeclared intervals hold the previously committed value, not an implied jump.

## Values and easing

| Property | Type/unit | Examples |
|---|---|---|
| position | Local 2D pixel translation | `offset(-250, 0)`, `(100, 20)` |
| scale | 2D scale | `factor(0.2, 0.2)`, `(1, 1)` |
| rotation | Degrees | `offset(15)`, `0` |
| opacity | 0–1 | `0`, `base` |
| fill | Straight linear RGBA fill | `rgba(1, 0.1, 0, 0.8)`, `base` |
| stroke | Straight linear RGBA stroke | `rgba(0, 0, 0, 1)`, `base` |
| blur | 0–512 | `0`, `base` |
| stroke-width | 0–4096 | `factor(2)`, `base` |
| path-progress | Explicit literal 0–1 | `0`, `1` |
| mask-rectangle-top-left / mask-rectangle-bottom-right | Project-coordinate rectangle corners | `base`, `offset(80, 0)` |
| mask-position | Independent project-coordinate translation | `offset(120, 0)` |
| mask-scale | Independent 2D scale around the fixed mask pivot | `factor(1.2, 0.8)` |
| mask-rotation | Independent rotation, degrees | `offset(30)` |
| mask-node(c,n).position | Existing node position in project coordinates | `base`, `offset(40, 0)` |
| mask-node(c,n).in-handle / out-handle | Handle offset relative to its node | `offset(10, -20)` |

`base` is the pre-application base value, not animated playhead evaluation or an accumulated previous segment. `offset(...)` adds to base; `factor(...)` multiplies it. Vectors apply per component, preserving nonuniform scale. Subtitle stroke base comes from its style.

Fill/stroke are complete color variables. `rgba(...)` RGB is linear and permits HDR; alpha is 0–1. Only rgba/base are allowed, not color offset/factor. Base reads target style color. UI HEX is sRGB, unlike DSL linear values. `#` remains comments; HEX literals are not DSL expressions.

Project colors are `{ "red": ..., "green": ..., "blue": ..., "alpha": ... }`. Legacy v3 channels merge by time union, using style bases for missing channels and retaining component easing/trim phase. Main app/worker share Core serialization/evaluation.

Position is local translation only, excluding parent/path/natural layout; it does not change ink bounds, anchor, pivot, or font size. Project v5 position/scale each use one vector track with `{ "x": ..., "y": ... }` values. Legacy components migrate by time union with independent easing; new writes do not emit component tracks.

```text
segment enter fixed 300ms
    at 0 position offset(-250, 0) ease-out
    at 0 scale factor(0.2, 0.2) ease-out
    at 0.7 scale factor(1.08, 1.08) ease-in-out
    at 1 position base
    at 1 scale base
end
```

Interpolation: `hold`, `linear`, `ease-in`, `ease-out`, `ease-in-out`, `power(exponent)`, default linear. The exponent is a finite positive number; `power(2)` accelerates and `power(0.5)` decelerates. A point's easing governs the interval to the next point of that complete target. POWER uses the same evaluator and retained trim phase as panel-authored keys and native ASS rectangle transforms.

## Clip masks and node selectors

A script animates existing Clip mask geometry. Create a rectangle or close a vector contour in the workbench before applying it. Rectangle-corner properties require a rectangle; node selectors require a vector mask. Applying a script to absent geometry reports the declaring line and column and changes neither the project nor Undo history.

Selectors use one-based contour and node numbers: `mask-node(1,1)` means the first node in the first contour. Spaces inside the parentheses are allowed. Compilation resolves this portable selector to that Clip's stable node ID; two nodes' `position` properties remain separate targets through editing, saving, reloading and timing changes. Moving a node keeps its handles relative to it. The script's `base` reads the original node or handle value.

Node position or handle tracks lock node count, contour membership, ordering and closure. Clear node morph tracks to unlock topology; independent mask translation, scale and rotation remain. The fixed mask pivot is a geometry setting and does not change when nodes morph. Masks stay in project coordinates while the subtitle moves, rotates or scales.

Applying a script replaces only tracks with the same complete target, preserving other nodes and ordinary subtitle properties. Keyframe and ordered-transform representations are mutually exclusive. A script cannot implicitly replace an ordered ASS transform program: clear that target track explicitly first. The script does not create or copy mask geometry.

## Validation and project boundaries

- IDs/segment names start with lowercase ASCII and may contain lowercase letters, digits, `.`, `-`; maximum 64 characters, no duplicate segment names.
- Maximum 128 segments, 4,096 source points, 262,144 source characters. Fixed segments up to 24 h; flex weights greater than 0 and at most 1,000.
- Only documented instructions/values: no file/network/C#/loops/arbitrary execution.
- Unknown versions/properties/easing, wrong dimensions, nonfinite/out-of-range values fail. Text errors carry line/column; compilation validates shared endpoints and target bases.
- Compile within the clip's valid content clock, including trimmed AnimationOffset. End points do not extend half-open playback intervals.
- `ProjectEditor.ApplyEffectScript` is one atomic transaction preserving subtitle/track identity, path, and unmentioned properties. Failure changes neither project nor Undo; generated points remain editable/saveable/reloadable/movable/trimmable.
