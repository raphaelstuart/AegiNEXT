# Platform publishing

[English](publishing.md) | [简体中文](zh-CN/publishing.md)

Development product version stays **0.1.0**; build identity/time/hashes belong in package-manifest.json. Packages contain self-contained .NET 10, main app, independent worker, FFmpeg/FFprobe, project native modules, and third-party runtime closure.

Build on the corresponding Mac architecture:

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
```

Build Windows locally with .NET SDK/x64 MinGW/FFmpeg shared/SDL3 SDK. Parallels ARM64 hosts may explicitly target and run x64:

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

Output must be new. SkipBuild reuses matching RID/Configuration native output but republishes both managed apps and validates SDKs. Publishing does not install/replace dependencies. Windows defaults to win-x64, with isolated RID obj avoiding Mac/ARM64 NuGet artifacts. Selected build tests build their project after solution build; Release|Any CPU does not enable all tests, so no presumed no-build DLL.

## Locks and RID

Generic restores use packages.lock.json; explicit RIDs use packages.osx-arm64.lock.json/packages.osx-x64.lock.json/packages.win-x64.lock.json. Restore/build/test/both publish pass `-p:AegiNextRuntimeIdentifier=<RID>` for early evaluation; `-r` alone may set plural RuntimeIdentifiers too late.

On first lock maintenance/dependency updates, Mac/Linux run `dotnet restore AegiNext.sln --force-evaluate` for generic, and `dotnet restore AegiNext.sln -r <RID> -p:AegiNextRuntimeIdentifier=<RID> --force-evaluate` for each RID. Review/version all locks. Windows defaults win-x64; generic maintenance needs empty `-p:RuntimeIdentifier=`. Normal builds stay locked.

## Native closure, licenses, identity

macOS scans actual Mach-O imports/minimums, copies non-system recursive dependencies into Contents/Frameworks, rewrites relative paths, and signs/verifies. SigningIdentity defaults `-` ad-hoc; release notarization is separate. LSMinimumSystemVersion uses the highest collected actual requirement, not CMake's 14.0 setting. Local Homebrew currently may require macOS 27; these packages do not claim 14 support.

Windows uses MinGW objdump for x64 PE imports. It resolves from selected SDK/native outputs and validated x64 g++ directory, follows compiler symlinks, and collects original prefix licenses without whole-PATH searches. Tools have adjacent dependencies. API Sets/system DLLs come from Windows; third-party VC/MinGW runtimes are packaged. Recognized OS dependencies include [Ncrypt](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenstorageprovider), [Avicap32](https://learn.microsoft.com/en-us/windows/win32/api/vfw/nf-vfw-capcreatecapturewindowa), and [FontSub](https://learn.microsoft.com/en-us/windows/win32/api/fontsub/nf-fontsub-createfontpackage).

Use RuntimeDependencyDirectory for explicit additional third-party runtime locations; no copying from arbitrary system installations. Their original notices are required and may be supplied in named LicenseDirectory subfolders. Original LICENSE/COPYING/NOTICE sources/hashes are recorded; missing native notices fails publishing.

media-runtime.json selects package tools only, without development fallback. Unmarked development runs accept explicit AEGINEXT_FFMPEG_PATH/AEGINEXT_FFPROBE_PATH or PATH.

The external package-manifest.json records version/RID/Git SHA/dirty/time, actual packaged tool version first lines, both includedFrameworks, native/license origins, policies, and post-signing file hashes, excluding its own recursive hash. Unreadable Git means unavailable/null SHA/dirty, never a false clean claim. Tool identities are read after closure/signing.

Windows RequiredRuntimePolicy follows [.NET 10 supported OS policy](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md), including lifecycle and Windows 11 ARM64 x64 emulation, checked 2026-10-04. Build-host version is not runtime acceptance or a native minimum. Release acceptance removes developer PATH and installed .NET/FFmpeg assumptions, then tests open/play/audio/subtitles/actual export/cancel separately per OS.

## Language resources

The desktop project copies UTF-8 JSON without a BOM from `src/AegiNext.Desktop/I18n/Languages/` to `AppContext.BaseDirectory/i18n/` for build, test and publish outputs. The package payload is `AegiNext.app/Contents/MacOS/i18n/` on macOS and `AegiNext/i18n/` on Windows. Include `en-US.json` and `zh-CN.json`; each root must contain nonempty `LanguageName`, the corresponding valid culture `LanguageID`, and a `Strings` object whose values are strings. Additional language filenames may differ from their IDs; all first-level JSON packs must have unique IDs. The application loads these files at startup and needs a restart after changes. See [workspace localization](composable-workspace.md#localization) for the API, XAML bindings and language matching rules.

`verify-package.ps1` checks the payload language directory, both built-in files, UTF-8 encoding, JSON shape, metadata, string entries and duplicate IDs/keys in addition to the existing complete file-hash inventory. Language JSON remains in that inventory; changing packaged language files requires a fresh manifest and the normal package/signing workflow to pass verification. These resource checks do not establish native visual acceptance of translated controls or platform menus.

## Relocation checks

Move the whole package, then verify read-only; missing/extra/changed files fail:

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

macOS non-Mach-O .NET signatures include extended attributes; archives/copies must preserve them. Finder/ditto do; content-only copies can preserve hashes yet lose signatures:

```sh
ditto -c -k --sequesterRsrc --keepParent artifacts/releases/mac-local artifacts/releases/mac-local.zip
ditto -x -k artifacts/releases/mac-local.zip '/path/with spaces/moved'
codesign --verify --deep --strict '/path/with spaces/moved/mac-local/AegiNext.app'
```

Historical package evidence: [interaction/release](README.md#implementation-evidence), [subtitle/vector/DSL updates](README.md#implementation-evidence), and [color workspace](README.md#implementation-evidence). Current GPU/title/color package evidence is [here](checkpoints/export-title-colors.md). Later track/highlight source tests are [separate](checkpoints/track-styles-and-karaoke.md); that record does not claim rebuilding complete release ZIPs.
