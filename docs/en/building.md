# Build and run

[English](building.md) · [简体中文](../zh-cn/building.md) · [All guides](README.md)

## Set up

Use .NET SDK **10.0.401** (stable patches in the same feature band) and **PowerShell 7.2+**. Native workbench builds support macOS `osx-arm64`/`osx-x64` and Windows `win-x64`; Windows ARM64 hosts target x64. Linux native media/runtime support is deferred.

Native builds need CMake, Ninja, a compiler, the shared FFmpeg SDK, and SDL3. macOS uses Homebrew and the Apple SDK; Windows uses Scoop and x64 MinGW. Versions are defined in `ffmpeg-toolchain.json` and native dependency manifests; a CLI-only FFmpeg distribution is insufficient. Current media locks are FFmpeg 9.0.2 and SDL3 3.4.16.

Run from the repository root:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

If recognized packages are missing, explicitly add `-InstallDependencies`. The check does not install, restore, or build. Invalid installed versions are not automatically replaced. The actual minimum macOS version depends on collected native libraries, even when the project deployment target is lower.

## Launch

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

Replace both RIDs with `win-x64` on Windows or `osx-x64` on Intel Macs. Explicit Windows build:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release
```

## Debug with Rider

Open `AegiNext.sln`. Before the first Debug run or after native/ABI changes, build the matching libraries:

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

Then run `AegiNext.Desktop` in Debug. Ordinary dotnet/Rider builds copy existing native output and do not compile missing libraries. Debug and Release are isolated; stop the old process after rebuilding. Export errors ANX1001/ANX1002 indicate absent/stale native contract metadata: rebuild the matching Workbench.

## Build options

| Target / option | Purpose |
|---|---|
| `Managed`, default | Build managed projects and copy existing native output |
| `Workbench` | Decoder → Audio → Export → Managed |
| `Decoder`, `Audio`, `Export` | Build one independent native component |
| `Native`, `All` | Optional macOS HDR diagnostic; All adds Managed, not Workbench |
| `-FfmpegRoot`, `-SdlRoot` | Explicit shared SDK locations |
| `-WithMediaTools` | Check PATH FFmpeg/FFprobe for a managed build |
| `-ReportPath` | Save an environment report |

FFmpeg SDK resolution: explicit root → `FFMPEG_DIR` → package-manager prefix. SDL uses explicit root → `SDL3_DIR` → package-manager prefix. Invalid explicit paths fail. Development tools use absolute `AEGINEXT_FFMPEG_PATH`/`AEGINEXT_FFPROBE_PATH` or PATH; complete packages use their own tools.

Native output is `artifacts/native/<RID>/<Configuration>/`; intermediate managed files use `obj/<RID>`. NuGet versions live in `Directory.Packages.props`, with ordinary restore and no package lock files. Release builds use `AegiNext.Product.slnf`; Debug uses `AegiNext.sln`.

## Run focused tests

```powershell
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~EffectScript'
```

Valid project filters: Core, Application, Rendering, Media, Desktop, Desktop.Ui. No tests run without `-RunTests`; Workbench testing also runs the corresponding native CTest suites. Release restores only selected test projects.

Real media/device tests require matching native output and explicit switches: `AEGINEXT_RUN_DECODER_TESTS=1`, `AEGINEXT_RUN_AUDIO_TESTS=1`, and for the actual system device `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`, plus real tool paths. Otherwise native cases skip. See [Media](media.md).

Build-script QA:

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

The first command restores pinned QA tools to project artifacts. Headless UI and silent audio tests do not establish native appearance or audible latency. Package the result with [Publishing](publishing.md).

## Profile video export

Set `AEGINEXT_EXPORT_PROFILE=1` before starting `aegn-exporter` to write an `AEGINEXT_EXPORT_PROFILE` JSON line to stderr after a successful native export. It reports frame count, native elapsed time, decoder/download time, rendering, overlay coverage, YUV resampling, composition, encoding, and muxing. Overlay counters distinguish updates, retained revisions, active pixels, and bounded foreground color preparation. The accompanying `AEGINEXT_RENDER_PROFILE` line separates evaluation, drawing, and F32 copying, and reports copied bytes and scratch-surface allocations/reuse. The normal worker protocol remains on stdout. Without the switch, stage clocks are disabled.

Capture worker stderr directly when diagnosing performance; the desktop exporter consumes it. Measure complete export wall time separately, including worker startup and final remux. Use the same media, project snapshot, configuration, and build mode when comparing FPS. Internal resampling and composition use at most four threads per stage; GPU encoding can still be limited by CPU composition.

Export ABI 5 explicitly reports updated, unchanged, or empty subtitle foreground. Native export retains its F32 buffer and reuses prepared coverage for stable revisions. Consecutive updates use the scalar composition path without rebuilding coverage; the first subsequent unchanged frame prepares coverage once. Video backgrounds still pass through the same full-frame YUV resampling. Rebuild the application and worker together after an ABI change.

One-frame decode prefetch is automatic after the first frame confirms the VideoToolbox hardware backend. Software and other backends remain serial by default. Set `AEGINEXT_EXPORT_PREFETCH=0` to disable prefetch or `1` to force it for any backend; other explicit values also select serial decoding. The producer owns decoding and snapshots frame color metadata before publication. Rendering and encoding remain on the export thread. `read_ms` measures decoder service time, which may overlap later stages; `prefetch_wait_ms` measures the export thread's actual wait. Compare complete wall time rather than adding overlapping stage durations. The profiler reports the queue capacity and peak queued/in-flight frames.

On macOS, a native build with `BUILD_TESTING=ON` also creates `aeginext_export_metal_tests`. Run it with `--benchmark 8` to measure the experimental SDR Metal compositor, including uploads, synchronization, readback, and the same CPU resampling. This standalone research target does not enable GPU composition in normal exports. It reports numerical differences against the CPU oracle; any difference fails the default bit-exact activation requirement. HDR and decoder/encoder GPU-surface interoperability are outside this prototype.
