# Compiler checks and source map

Paths below are relative to the AegiNext checkout, not to this reference file.

| Responsibility | Source |
|---|---|
| Text grammar and line/column diagnostics | `src/AegiNext.Core/Effects/EffectScriptParser.cs` |
| Dimensions, budgets, segment invariants | `src/AegiNext.Core/Effects/EffectScriptValidator.cs` |
| Exact allocation, base resolution, endpoint merging | `src/AegiNext.Core/Effects/EffectScriptCompiler.cs` |
| Animation ranges after cropping | `src/AegiNext.Core/Editing/LayerAnimationTiming.cs` |
| Built-in script source | `src/AegiNext.Core/Effects/Scripts/*.aegifx` |
| Atomic workbench application | `src/AegiNext.Application/ProjectEditor.Effects.cs` |
| Personal library, conflicts, import/export | `src/AegiNext.Application/Presets/EffectScriptPresetService.cs` |
| Editor grammar projection | `src/AegiNext.Desktop/Controls/Editing/EffectScriptLanguage.cs` |
| Editor input and completion | `src/AegiNext.Desktop/Controls/Editing/EffectScriptEditor.axaml.cs` |

Identifiers/segment names begin with a lowercase ASCII letter and allow lowercase ASCII letters, digits, dot, and hyphen, up to 64 characters. Names cannot repeat within a script. Current budgets: 128 segments, 4,096 source keys, 262,144 source characters; fixed durations are positive and at most 24 hours; flex weights are positive and at most 1,000. Durations, progress, and weights use decimal precision of at most six places. Scalar/component limits come from `AnimationPropertyMetadata`; re-read them before proposing extremes.

## A short-clip-safe starting pattern

```text
effect "personal-soft-slide" version 1
short-clip compress

segment enter fixed 250ms
    at 0 position offset(-80, 0) ease-out
    at 0 opacity 0 ease-out
    at 1 position base
    at 1 opacity base
end

segment hold flex 1
    at 0 position base hold
    at 0 opacity base hold
    at 1 position base
    at 1 opacity base
end

segment exit fixed 250ms
    at 0 position base ease-in
    at 0 opacity base ease-in
    at 1 position offset(80, 0)
    at 1 opacity 0
end
```

At 3 seconds the fixed parts remain 250ms each and the hold takes 2.5s. At 500ms the hold disappears. At 200ms both fixed parts compress to 100ms. The zero-length hold still has identical values at both ends. With `reject`, 200ms is intentionally invalid.

Frequent mistakes: copying absolute clip times into `at`, forgetting one property's `at 1`, different shared endpoint values, a changing flex segment that collapses under compression, mistaking byte RGBA for linear RGBA, claiming `base` is an evaluated animation value, or using a built-in reserved ID for a personal import.

## Scoped regression entry points

Run serially from the checkout root; adjust configuration/RID to the established platform build. Do not install or change dependencies merely to run these commands.

```sh
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectScriptTests|FullyQualifiedName~EffectScriptColorTests'
dotnet test Tests/AegiNext.Application.Tests/AegiNext.Application.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectScriptEditingTests|FullyQualifiedName~EffectScriptPresetTests'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~EffectScriptLanguageTests
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EffectScriptSettingsUiTests|FullyQualifiedName~BuiltinEffectScriptUiTests'
```

Add a behavior test for the exact new source when changing application code or a reusable shipped example. Existing tests exercise their own sources, not arbitrary user-provided files. Real UI validation must also check input method preedit, keyboard completion, caret placement, read-only built-ins, and invalid-source preservation; close every test window.
