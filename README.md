# AegiNext

[English](README.md) | [简体中文](README.zh-CN.md)

Cross-platform subtitle timing, visual effects, and video export.

Start with the [Quick Start](docs/quick-start.md). User guides and developer documentation live in the [documentation index](docs/README.md); implementation evidence is recorded in [docs/checkpoints](docs/checkpoints/track-styles-and-karaoke.md).

AegiNext **0.1.0 is a development preview**. It supports video and audio playback, subtitle editing and timing shortcuts, waveform and spectrogram navigation, project storage, undo/redo, keyframes, motion paths, reusable effect scripts, portable subtitle style presets, and export in an independent process. Subtitle text sizes naturally to its content. Fonts, size, fill, stroke, alignment, anchor, pivot, pixel offset, and per-character highlighting are editable; the application has English and Chinese interfaces, light/dark themes, configurable accent and audio colors, and customizable shortcuts.

Seven fixed panels—Preview, Timeline, Subtitles, Styles, Effects, Export, and Log—can dock, float, hide, and form named layouts. Subtitle tracks have stable identities and names; clips on the same track cannot overlap. The minimap and touchpad/wheel gestures control the timeline viewport. Select a clip or keyframe in the timeline, edit the picture directly in Preview, and edit its data in Effects. Project files use **v3**; v1/v2 projects are explicitly unsupported. Personal layout and shortcut migration is separate from project storage.

Editing previews are SDR. PQ/HLG export composites original high-precision video frames into HEVC 10-bit output and preserves supported HDR signaling. Earlier 0.1.0 packages were tested on macOS and Parallels Windows 11 for media opening, audio, actual export, cancellation, and relocation. Package evidence and source-only updates have separate scopes: see [publishing](docs/publishing.md), [export](docs/export.md), and the corresponding checkpoints. Touchpad feel, multiple monitors, cross-DPI behavior, and physical Windows GPU encoding require their own acceptance evidence.

## Development environment

- .NET SDK `10.0.401`; stable patches in the same feature band are allowed.
- Avalonia `12.1.3`; direct and transitive dependencies are centrally versioned in `Directory.Packages.props` and project lock files.
- SkiaSharp / SkiaSharp.HarfBuzz `3.119.4`; HarfBuzzSharp and the participating native packages are locked to `8.3.1.5`.
- Development release targets: Apple Silicon macOS and Windows x64. The current local macOS dependency closure requires 27.0; each package records its actual minimum in its manifest.
- PowerShell 7.2+. Dependency installation uses Homebrew on macOS and Scoop on Windows. Missing package managers are reported with setup instructions; scripts do not execute remote installers automatically.

From the repository root:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
pwsh -NoProfile -File ./build.ps1 -Target Workbench -InstallDependencies
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application,Desktop
```

`Workbench` checks and builds Decoder → Audio → Export → Managed, producing the complete development workbench. The default target remains `Managed / Release`. Ordinary builds only check dependencies; installation requires `-InstallDependencies`. `-RunTests` accepts a project filter. SDK requirements, parameters, locks, and platform limitations are documented in [building](docs/building.md).

Launch with the same RID, for example on Apple Silicon:

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

On Windows, replace both RID arguments with `win-x64`. Closing the main window exits the process. Rider can open `AegiNext.sln` and run `AegiNext.Desktop`.

### Native libraries before Debug runs

**Debug builds and Rider debugging do not automatically build missing native media libraries.** Ordinary `dotnet build` / `dotnet run` and the `Managed` target only copy native output that already exists for the selected RID and configuration. Managed compilation can succeed without those libraries; playback or export then reports a native library load error.

Before the first Debug session, and after changing native source or its ABI, run from the repository root:

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows, including ARM64 hosts:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

Use the command for your platform. `build-debug-native.ps1` runs the existing build pipeline with `Workbench / Debug`: Decoder → Audio → Export → Managed, including copying the native libraries into the application output. On macOS, it selects the host RID automatically (`osx-arm64` on Apple Silicon). Native artifacts are isolated by RID and configuration: rebuilding Release does not update Debug libraries. After a successful build, stop the previous debug process and start `AegiNext.Desktop` in Debug. The script accepts `-CheckEnvironment`, explicit `-FfmpegRoot` / `-SdlRoot`, `-InstallDependencies`, and `-RunTests -TestProjects Media`; installation and tests require their explicit switches. See [building](docs/building.md#debugrelease-native-export-compatibility) for configuration and ABI details.

Playback and export use separate FFmpeg/SDL3 native modules. Development runs accept PATH tools or absolute `AEGINEXT_FFPROBE_PATH` / `AEGINEXT_FFMPEG_PATH`; an invalid explicit path fails. Complete packages include their own tools and dependency closure. `-Target All` builds the optional HDR diagnostic and Managed, and does **not** include Workbench; the native HDR display backend currently supports macOS only.

## Repository structure

| Location | Responsibility |
|---|---|
| `src/AegiNext.Core` | Pure C# rational time, media facts, project v3, subtitles/tracks, scene layers, animation, paths, and strict validation. |
| `src/AegiNext.Application` | Editing transactions, undo/redo, trim/stretch/split/merge, atomic storage, resource import/relocation, and SRT/TXT adapters. |
| `src/AegiNext.Desktop` | Avalonia workbench, explicit session ownership, playback, dock layouts, panels, shared controls, settings, menus, and optional HDR diagnostics. |
| `src/AegiNext.Rendering` | UI-independent Skia/HarfBuzz shaping and linear F16 scene rendering; see [rendering](docs/rendering.md). |
| `src/AegiNext.Media` | Frame validation, native ABI ownership, FFprobe, decoding, playback, SDR conversion, audio analysis, and independent export orchestration. |
| `src/AegiNext.ExportWorker` | Windowless export process using the same scene evaluator and renderer, source timestamps, and supported HDR semantics. |
| `Tests/` | Focused domain, application, rendering, media, controller, headless UI, and build-script tests; rendering fixtures pin font bytes and licenses. |
| `native/decoder`, `native/audio`, `native/export` | Independent FFmpeg decode, SDL3 audio, and high-precision export C ABIs with contract tests. |
| `native/` | Optional Objective-C++ / libplacebo / MoltenVK HDR diagnostic and native contracts. |
| `build.ps1`, `scripts/build/`, `publish.ps1` | Environment checks, explicitly requested installation, builds/tests, and platform packaging. |
| `docs/`, `docs/zh-CN/` | English and Simplified Chinese user and developer documentation. |

## Engineering conventions

Use Allman braces, file-scoped namespaces, one top-level type per file, `var`, and target-typed `new()` where appropriate. Nullable checking, recommended .NET analyzers, and warnings-as-errors are enabled. Enum constants follow `ALL_UPPER`; conflicting CA1707 suppression is local to the relevant enum file.

Keep layout space, feature panels, and reusable controls separate. Business view models must not hold controls, Dock objects, or bitmaps. Record focused tests, native runs, and outstanding manual acceptance separately at each checkpoint. Generated builds, reports, and screenshots stay in ignored directories; dependency locks are versioned.

Project-specific Codex skills are available as `$aeginext-effect-dsl` for writing subtitle scripts and `$aeginext-controls` for developing and using shared controls. Their portable sources live in [.agents/skills](.agents/skills/aeginext-controls/SKILL.md).

## Commit conventions

Use a short English `type: summary`, with a space after the colon: `feat` for features, `fix` for defects, and `chore` for maintenance.

```text
feat: modernize workbench menus, settings and style presets
fix: restore keyframe selection and parameter editing
```

Keep commits scoped; inspect the index and preserve unrelated work. Source, documentation, relevant tests, lock files, `AegiNext.sln`, and `.csproj` files are versioned. Do not commit `bin/`, `obj/`, `TestResults/`, `testResults.xml`, `artifacts/`, IDE state, or local screenshots. Run `git diff --check` and affected builds/tests; do not treat compilation as platform runtime or visual acceptance.
