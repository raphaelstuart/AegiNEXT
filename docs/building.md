# Cross-platform building

[English](building.md) | [简体中文](zh-CN/building.md)

## Entry point and targets

The repository entry is `build.ps1`; shared implementation lives in `scripts/build/AegiNext.Build.psm1`. It resolves `global.json` and the solution relative to the script's repository from any working directory. Paths/arguments are passed as arrays, never executable command-string concatenation.

PowerShell 7.2+ is required:

```powershell
# Windows: install from Windows PowerShell, then run project scripts with pwsh.
scoop install main/pwsh
# macOS
brew install powershell
```

See official [Scoop](https://scoop.sh/) or [Homebrew](https://brew.sh/) setup. Scripts do not change execution policy, execute remote package-manager bootstrap code, or install Apple/Microsoft SDK installers.

| Platform | Managed default | Native / All | Decoder | Explicit dependency installation |
|---|---|---|---|---|
| macOS | .NET solution | Optional HDR diagnostic; All builds native then managed | Separate FFmpeg software decoder and CPU SDR preview | Homebrew |
| Windows x64, including ARM64 hosts targeting x64 | .NET solution, win-x64 | HDR backend unavailable: explicit failure | FFmpeg decoder/SDR preview; tested on Parallels Windows 11 | Scoop |
| Linux | Managed build entry; product runtime unaccepted | Unavailable: explicit failure | Deferred: explicit failure | No distribution manager chosen automatically |

Default is `Managed / Release`: no HDR validation or CMake/MoltenVK/libplacebo requirement. Managed compilation alone needs no media tools. `-WithMediaTools` checks PATH FFprobe/FFmpeg; Decoder always validates its chosen development SDK. HDR native uses AppKit/Objective-C++/CAMetalLayer and builds only on macOS; Windows Native never silently becomes Managed success.

## Commands

From the repository root:

```powershell
# Check only: no installation, restore, or build.
pwsh -NoProfile -File ./build.ps1 -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -CheckEnvironment
# Default managed build.
pwsh -NoProfile -File ./build.ps1
# Complete first-version workbench, excluding optional HDR display.
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
# Windows x64 output from x64 or ARM64 host; native uses x64 MinGW.
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release
# Install recognized missing packages, recheck, then build.
pwsh -NoProfile -File ./build.ps1 -InstallDependencies
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -InstallDependencies
# Selected tests only; no tests without -RunTests.
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Media
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Desktop
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application
# Optional macOS HDR native plus Managed, or native only.
pwsh -NoProfile -File ./build.ps1 -Target All -Configuration Release -RunTests -TestProjects Media
pwsh -NoProfile -File ./build.ps1 -Target Native -Configuration Debug -RunTests
# Independent component check/build/tests, then copy via Managed.
pwsh -NoProfile -File ./build.ps1 -Target Decoder -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Decoder -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Audio -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Export -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Managed
# Shared SDK, not an ffmpeg.exe-only distribution.
pwsh -NoProfile -File ./build.ps1 -Target Decoder -FfmpegRoot 'C:\SDKs\ffmpeg-9.0.2-full_build-shared' -CheckEnvironment
# Optional report; relative paths use the caller's working directory.
pwsh -NoProfile -File ./build.ps1 -CheckEnvironment -ReportPath artifacts/verification/build-environment.json
```

`-CheckEnvironment` and `-InstallDependencies` are mutually exclusive. Reports are written only with `-ReportPath`; ordinary checks create no build directory. Incomplete environment exits 2, invalid parameters 1; build/test/package-manager failures preserve the external exit code and stop later steps.

Managed `-RunTests` defaults to Core/Application/Rendering/Media/Desktop. Filter with `-TestProjects`; in PowerShell, `Media,Desktop` selects both. Desktop controller tests open no windows. Native/Decoder/Audio/Export run their own CTest; All combines HDR CTest with selected managed tests, never opens the diagnostic GUI, and does not include Decoder/Audio/Export. Linux explicitly fails selected Rendering tests because required native assets are absent.

Workbench checks the entire dependency set and builds Decoder → Audio → Export → Managed. Tests run the three native CTest suites before selected managed projects. Checks include FFmpeg/swresample, SDL3, SDK/native RID agreement, plus the selected SDK's actual libx264/yuv420p, libx265/yuv420p10le, AAC/fltp, MP4, and Matroska capabilities. Missing capability is Invalid, not silently replaced. Only Missing items are installable with the explicit switch. Workbench supports macOS/Windows x64; Linux is deferred. Windows native/audio/worker/cancellation evidence is in the [historical interaction release checkpoint](README.md#implementation-evidence).

### Debug/Release native export compatibility

Native outputs are isolated by RID **and configuration**. Rebuilding Release does not update the libraries used by a Debug session in Rider. The GPU export request uses ABI 2 for both CPU and GPU encoding; an old ABI 1 library fails before either encoder can start.

For a Debug session, rebuild the matching workbench before launching it:

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows, including ARM64 hosts:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

`build-debug-native.ps1` is a dedicated `Workbench / Debug` entry point: it reuses the existing environment checks and builds Decoder → Audio → Export → Managed so the application output receives the native libraries. It accepts the RID, FFmpeg/SDL SDK roots, jobs, environment report, dependency installation, and focused test options from `build.ps1`. Run with `-CheckEnvironment` for a read-only check. Ordinary Debug builds and Rider sessions still do not invoke native compilation automatically.

The export CMake build writes `aeginext_export.contract.sha256` only after its native library has built successfully. Managed builds compare this fingerprint with the current public export header before copying native output. `ANX1001` means the library lacks current contract metadata; `ANX1002` means the interface has changed since that library was built. Both errors include the RID, configuration, library path, and repair command. A managed-only checkout without a native export library can still compile, but needs a complete Workbench build for export.

The worker also checks the actual loaded ABI and managed request size. Its mismatch error reports both ABI versions, structure sizes, process architecture, and the exact library path. Do not bypass this check or mix native files between Debug/Release or platforms.

`Directory.Build.props` fixes product version `0.1.0` and assembly/file version `0.1.0.0`. RIDs are `osx-arm64`, `osx-x64`, `win-x64` and must match the platform. macOS uses host architecture; Windows always targets x64. ARM64 Windows .NET/PowerShell hosts can generate x64, but native compilation requires x64 MinGW. Windows executes x64 through OS emulation.

## Checks and installation

Managed checks execute `dotnet --version` inside the repository. `global.json` requires 10.0.401, `latestPatch`, no previews: a stable patch in that feature band is valid; 10.0.400/10.0.500/previews are not replacements. An existing command with failed SDK resolution gets a matching-SDK diagnostic.

Missing SDK installation uses Homebrew `dotnet` or Scoop `main/dotnet-sdk`; names were checked against [Homebrew](https://formulae.brew.sh/formula/dotnet) and [Scoop](https://github.com/ScoopInstaller/Main/blob/master/bucket/dotnet-sdk.json). An already usable SDK need not be package-manager-owned. A missing package manager is advisory for Managed, blocking only requested installation.

`-WithMediaTools` executes selected PATH tools' `-version` and validates release text plus compiled/runtime libavutil/libavcodec/libavformat/libswscale/libswresample against `src/AegiNext.Media/Probing/ffmpeg-toolchain.json`. Locked versions include FFmpeg **9.0.2**, swscale **10.1.102**, swresample **7.1.102**. Accepted release strings are `9.0.2` and `9.0.2-full_build-www.gyan.dev`; other suffixes/development versions fail. Missing version/library or tool failure is Invalid.

Explicit installation of missing CLI tools uses Homebrew `ffmpeg` or Scoop `main/ffmpeg`, once for both binaries. Linux tools are user-provided. The switch may accompany Native but cannot change its platform scope; it does not validate filters/hardware encoding/libplacebo encoding.

macOS Native additionally checks macOS 14+, PowerShell host architecture, SDK/native RID agreement for All, CMake minimum from `native/CMakeLists.txt`, Ninja/pkg-config/CTest when selected, and `xcrun` Clang plus actual Apple SDK. Missing SDK/license needs user setup, not Homebrew replacement. It verifies libplacebo/MoltenVK/Vulkan-Headers headers/libraries/pkg-config, locked versions from `native/dependencies.json`, and Vulkan/vk_proc_addr/shaderc capabilities.

Installation handles mapped **Missing** items only, then rechecks; installed **Invalid** versions are never automatically upgraded/downgraded. Future newest packages may not satisfy locks; scripts do not rewrite locks to fit the machine. Unimplemented Windows HDR does not trigger installation; Decoder is independent.

## Decoder SDK

Decoder is an independent `native/decoder` CMake project, orchestrated by `scripts/build/AegiNext.Decoder.ps1`. It needs neither .NET nor HDR dependencies. SDK resolution is explicit `-FfmpegRoot`, then `FFMPEG_DIR`, then `brew --prefix ffmpeg` or `scoop prefix ffmpeg-shared`. An invalid explicit directory fails without fallback. The parameter serves Decoder/Audio/Export/Workbench.

Checks cover component CMake minimum, Ninja/optional CTest, and:

- macOS: native arm64/x64 PowerShell, macOS 14+, xcrun Clang/Clang++/SDK.
- Windows: x64/ARM64 PowerShell with both gcc/g++ `-dumpmachine` reporting `x86_64-w64-mingw32`. Scoop `main/mingw` is used, not a Visual Studio requirement or host-CPU assumption.
- FFmpeg: exact avutil/avcodec/avformat/swscale/swresample development headers, macOS dylibs or Windows import libraries and major-version DLLs, and selected package's FFmpeg/FFprobe compiled/runtime versions.

Windows uses [Scoop ffmpeg-shared](https://github.com/ScoopInstaller/Main/blob/master/bucket/ffmpeg-shared.json), the Gyan shared development distribution with `FFMPEG_DIR`. Plain ffmpeg CLI cannot replace it; see [Gyan](https://www.gyan.dev/ffmpeg/builds/). Explicit install uses cmake/ninja/ffmpeg on macOS and main/cmake/main/ninja/main/mingw/main/ffmpeg-shared on Windows. Apple SDK remains user-managed. Newly installed Scoop packages can be found by prefix before PATH refresh.

## Audio, export, and process tests

Audio/Export independently build `native/audio` / `native/export` on macOS and Windows x64, reusing decoder/compiler/FFmpeg checks; Linux is deferred. Audio additionally locks SDL3 **3.4.16**. Resolution: `-SdlRoot`, `SDL3_DIR`, then Homebrew `sdl3` / Scoop `aeginext-sdl3`. Validate headers, CMake package, import library, runtime, and SDL compiled/runtime versions on device load.

Only explicit Audio/Workbench installation handles missing SDL. macOS uses Homebrew; Windows uses repository `scripts/build/scoop/aeginext-sdl3.json`, pinning the [official MinGW package](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.16), hash, and x86_64 SDK. Managed/Decoder/Export do not require SDL. Windows stages SDL3.dll and selected FFmpeg DLLs in the same RID/configuration output. Speaker behavior requires device evidence beyond build completion.

Workbench accepts `-SdlRoot`. Development tools may be on PATH or absolute `AEGINEXT_FFMPEG_PATH` / `AEGINEXT_FFPROBE_PATH`; builds do not modify persistent user environment.

Playback audio is 48 kHz stereo float PCM, up to 250 ms queued, usually 200 ms prefilled. Clock estimates submitted minus queued samples minus one device buffer; it is not an exact hardware DAC clock. Gaps/start offsets use silence; short audio continues silence through video end. No audio/device failure uses a monotonic video clock and publishes the error. Analysis separately decodes 16 kHz mono PCM.

Real audio tests require `AEGINEXT_RUN_AUDIO_TESTS=1`, absolute `AEGINEXT_FFMPEG_PATH`, and same-configuration Audio native output; otherwise they explicitly skip. Managed audio/controller/spectrum tests need no device. CTest uses SDL dummy and is not speaker acceptance.

`AegiNext.Media.TestHost` is a separate self-contained apphost copied with runtime into `ProbeTestHost`. It follows explicit test RID or SDK RID, rather than borrowing `DOTNET_HOST_PATH`, avoiding ARM64-SDK/x64-fixture conflicts. Argument, pipe, timeout, and cancellation tests still execute real processes.

## Output and configuration isolation

Native HDR cache: `artifacts/native/build-macos-<arch>-<configuration>`. Copyable native output: `artifacts/native/<rid>/<Configuration>`:

```text
artifacts/native/osx-arm64/Debug/libaeginext_media.dylib
artifacts/native/osx-arm64/Release/libaeginext_media.dylib
artifacts/native/osx-arm64/Release/libaeginext_decode.dylib
artifacts/native/osx-arm64/Release/libaeginext_audio.dylib
artifacts/native/osx-arm64/Release/libaeginext_export.dylib
artifacts/native/win-x64/Release/aeginext_decode.dll
artifacts/native/win-x64/Release/aeginext_audio.dll
artifacts/native/win-x64/Release/aeginext_export.dll
```

Decoder/Audio/Export caches use `build-<component>-<rid>-<configuration>` with `AEGINEXT_FFMPEG_ROOT`, `AEGINEXT_NATIVE_OUTPUT_DIR`, and Audio's `AEGINEXT_SDL_ROOT`. Windows stages only selected SDK bin DLLs for development, not CLI executables. Complete tools, recursive dependencies, licenses, and manifest come from `publish.ps1`.

Separate cache/output prevent Debug artifacts from misleading Release incremental builds. Media copies by explicit RID or SDK RID plus Configuration. Managed can build without native outputs; optional HDR reports missing load at runtime.

Before component configure, verify CMakeCache source/build paths, generator, C/C++ compiler. Changed compiler uses `--fresh` only on a verified component/RID/configuration cache with all explicit SDK parameters reapplied. Unchanged compiler preserves incremental state; ambiguous ownership fails without deletion.

`scripts/build-native-macos.sh` is a thin PowerShell wrapper, default Release, forwarding Configuration/RunTests. Old `AEGINEXT_NATIVE_BUILD_DIR` is rejected to preserve isolation. `AEGINEXT_NATIVE_BUILD_JOBS` remains supported, default 2.

Restore uses `--locked-mode`; build `--no-restore`; test uses matching configuration and builds the selected project. Default Release|Any CPU does not enable all test projects, so do not assume `--no-build` outputs exist. Explicit RID commands also pass `-p:AegiNextRuntimeIdentifier=<RID>` for early project evaluation. Restore `-r` alone may set only plural RuntimeIdentifiers; solution build cannot use `-r`, while project test/publish retain it with the property bridge.

Generic restore uses `packages.lock.json`; RID locks are `packages.osx-arm64.lock.json`, `packages.osx-x64.lock.json`, `packages.win-x64.lock.json`. Windows defaults to win-x64. RID intermediates use `obj/<rid>`, and all obj/bin directories are excluded from source discovery. Maintain all locks when dependencies change; normal builds never accept changed locked dependencies automatically. See [publishing](publishing.md). Temporary directories/search variables/caller LASTEXITCODE are restored on success/failure.

A macOS 14 deployment target cannot lower Homebrew dependencies' actual 27 requirement. Packaging records the greatest actual minimum and writes `LSMinimumSystemVersion`.

## Self-contained packaging

```powershell
# On the corresponding Mac architecture; builds Workbench and optional HDR by default.
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -OutputDirectory ./artifacts/releases/mac-local
# On Windows, including ARM64 hosts targeting x64.
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
# Read-only verification after moving the complete package.
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

Output must be new; missing SDK/runtime/original license fails. Packages contain self-contained .NET 10, main app/worker, tools, native modules, and recursive non-system dependencies, including VC/MinGW runtimes. macOS rewrites Mach-O paths and verifies signing. Windows resolves PE imports from selected SDK/validated x64 compiler/explicit dependency directories. `-SkipBuild` reuses matching native output but republishes both managed apps.

`media-runtime.json` selects package-local tools, without development fallback. Load diagnostics identify architecture/path and underlying file/dependency/architecture failure. `package-manifest.json` records 0.1.0, RID, Git SHA/dirty, time, actual tool versions, included runtimes, dependency/license origins, system policies, and post-signing hashes. Build-machine OS is not a proven minimum. See [publishing](publishing.md).

## Headless UI and script QA

`Desktop.Ui` is separate Avalonia Headless/xunit.v3, isolated from Desktop's xunit2 tests. It loads real XAML/controls/commands/input with controlled media and closes its own windows, without native desktop windows:

```powershell
dotnet test ./Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj -c Release
```

An absolute `AEGINEXT_UI_CAPTURE_DIRECTORY` enables layout PNG capture. Historical capture initially covered three settings pages; current settings has five. Headless is not native-menu/device acceptance. Linux's missing rendering native assets make this selection explicitly fail.

```powershell
# Explicit first download of pinned QA modules into project artifacts, not global installation.
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
# Cached offline tools.
pwsh -NoProfile -File ./scripts/test-build.ps1
```

This pins Pester 5.7.1/PSScriptAnalyzer 1.24.0, runs analysis then `Tests/Build`; ordinary builds need neither. Tests simulate installation/platform branches and do not replace real OS runs. Production scripts use full warning/error rules. Test-only exclusions cover cross-Pester-block unused variables and TestDrive fixture ShouldProcess; other rules remain. Reports: `artifacts/verification/build-scripts-tests.xml`.
