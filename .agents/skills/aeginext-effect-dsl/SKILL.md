---
name: aeginext-effect-dsl
description: Write, review, debug, or extend AegiNext subtitle effect scripts (.aegifx), including fixed entrance/exit timing, flexible holds, short-clip behavior, vectors, linear RGBA colors, and reusable templates. Use for AegiNext effect authoring or its parser/compiler/editor integration; do not use for unrelated shader or scripting languages.
---

# AegiNext effect authoring

Use the repository's actual DSL v1 parser and compiler as the authority. This is a declarative animation format, not C#, JavaScript, or an expression interpreter.

## Establish the authoring target

1. Locate the AegiNext checkout (`git rev-parse --show-toplevel` when already inside it). This repository skill lives at `.agents/skills/aeginext-effect-dsl/`; its resolved location is three directories below the checkout root. Resolve symlinks before deriving a root.
2. Read `docs/en/effect-dsl.md` (English) or `docs/zh-cn/effect-dsl.md` (Chinese), then the closest existing sample in `src/AegiNext.Core/Effects/Scripts/` or `docs/en/examples/effects/`. Read `references/authoring-checks.md` for failure cases and test entry points.
3. Determine the intended visual effect, fixed entrance/exit durations, changed properties, and behavior on clips shorter than the fixed total. Use an explicitly supplied policy; otherwise state the chosen `compress` policy with the delivered script. Ask only when a missing decision materially changes the effect.
4. Give personal scripts a unique lowercase stable identifier. Built-in IDs are reserved; retain an existing personal ID when editing that template. Display names belong to the template library and do not replace the source identifier.

## Write a duration-adaptive script

- Begin with `effect "personal-id" version 1`, then an explicit `short-clip compress` or `short-clip reject`.
- Put fixed transitions in `segment name fixed 300ms`; put the remaining duration in one or more `segment name flex 1` blocks. Close every block with `end`. At least one flex block and some keyframes are required.
- `at` positions are fractions of the current segment, not seconds. For each property present in a segment, write an `at 0` and an `at 1`, with strictly increasing intermediate positions. Different properties may be interleaved.
- Keep a property's shared endpoints exactly equal. A flex segment can become zero length under compression: its collapsed keyframes must all have the same value. Do not put a changing animation into a flex segment when promising arbitrary short-clip support.
- `base` reads the target's original base value, not the playhead, the previous segment, or accumulated effects. Prefer `offset(x, y)` for relative translation and `factor(x, y)` for relative scale; retain independent X/Y values.
- `position` and `scale` are complete two-component values. `fill` and `stroke` are complete straight linear RGBA values: `rgba(r, g, b, a)` or `base`. Colors do not accept `offset` or `factor`; RGB may be HDR, Alpha stays within 0–1.
- UI HEX/RGBA byte input is sRGB and differs from DSL linear color. Do not paste `#RRGGBB` into a value: `#` starts a comment. Convert intended sRGB colors to linear values when necessary and explain that conversion.
- Supported scalar properties are `rotation`, `opacity`, `blur`, `stroke-width`, and `path-progress`. The last accepts only an explicit 0–1 number. Use only documented units, functions, properties, and interpolations (`hold`, `linear`, `ease-in`, `ease-out`, `ease-in-out`).
- Do not add executable code, file access, loops, custom functions, per-axis property names, or rectangle/stretch semantics. Path animation requires an existing path; the script does not create its points.

## Validate the exact delivered source

Parsing alone cannot prove compatibility with a clip. Validate with `EffectScriptParser.Parse` and `EffectScriptCompiler.Compile` against representative real target layers and subtitle styles, including nonuniform transforms and nondefault colors. For color `base` on subtitle layers, pass the subtitle's `SubtitleStyle`.

Check a long clip, the exact fixed-duration total, a shorter clip, and a tiny positive clip. For `reject`, the insufficient durations must fail without modifying the project. Verify continuity, legal content-time bounds (including `AnimationOffset`), and base-relative values. Report what actually ran; do not claim an arbitrary script was validated because the built-in tests passed.

In the app, use **Settings → Effect Scripts → Validate** for parser diagnostics, save/export the personal template, then apply it from the workbench's Effects preset selector. Settings manages templates and has no apply-to-current-subtitle action. An imported `.aegifx` must pass validation and ID-conflict checks before changing the library.

When changing the language itself, update parser, validator, compiler, metadata, editor completion/highlighting, import/export, documentation, and scoped regression tests together. Preserve one atomic `ProjectEditor.ApplyEffectScript` transaction and unrelated animation properties.

## Deliver

Provide the complete UTF-8 `.aegifx` source or a clickable path to its saved file, policy and timing behavior, affected properties, and exact validation evidence. Use `docs/en/examples/effects/` for a requested reusable repository example; do not modify a built-in sample unless that is the requested scope.
