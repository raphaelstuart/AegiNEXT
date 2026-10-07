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

Valid project filters: Core, Application, Rendering, Media, Desktop, Desktop.Ui. No tests run without `-RunTests`; Workbench testing also runs its three native CTest suites. Release restores only selected test projects.

Real media/device tests require matching native output and explicit switches: `AEGINEXT_RUN_DECODER_TESTS=1`, `AEGINEXT_RUN_AUDIO_TESTS=1`, and for the actual system device `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`, plus real tool paths. Otherwise native cases skip. See [Media](media.md).

Build-script QA:

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

The first command restores pinned QA tools to project artifacts. Headless UI and silent audio tests do not establish native appearance or audible latency. Package the result with [Publishing](publishing.md).

## Documentation website

The MkDocs Material website builds directly from the two root README files and `docs/`. Use Python **3.13+** in an activated virtual environment (`python3` on macOS when `python` is unavailable):

```sh
python -m pip install -r scripts/docs/requirements.txt
python -m unittest discover -s Tests/Docs -v
python -m mkdocs build --strict
python -m mkdocs serve
```

The build output is `artifacts/docs-site/`. The preview command prints its local URL, including the `/Aegisub-NEXT/` project prefix. `AEGINEXT_DOCS_SITE_URL` overrides the website URL for custom domains or other deployment paths.

In the GitHub repository, set **Settings → Pages → Build and deployment → Source** to **GitHub Actions**. The **Documentation** workflow validates documentation pull requests, then builds and deploys updates on `main`; it can also be run manually. CI reads the deployment URL from GitHub Pages, so project prefixes and custom domains are reflected in the output. This build requires no application or native media dependencies.
