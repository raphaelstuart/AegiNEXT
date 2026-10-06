# Platform publishing

[English](publishing.md) | [简体中文](zh-CN/publishing.md)

The default development product version is **0.1.0**, from `Directory.Build.props`. Pass `publish.ps1 -Version 1.2.3` to publish a different version without editing the source. Versions have three numeric components, each between 0 and 65535. The publisher writes the same version into the main app and export worker assembly metadata, `package-manifest.json`, macOS bundle/DMG and Windows installer. **Help → About** reads the assembly product name and informational version and shows the application icon, current-year copyright and GPLv3 notice. Build identity/time/hashes belong in package-manifest.json. Packages contain self-contained .NET 10, main app, independent worker, FFmpeg/FFprobe, project native modules, and third-party runtime closure.

## Automatic release packages

Run the same entry point on either supported platform:

```powershell
pwsh -NoProfile -File ./release.ps1
```

`release.ps1` builds in `Release` configuration and calls the publisher with the native packaging option automatically: Windows creates an NSIS installer for `win-x64`, and macOS creates a DMG for the host's `osx-arm64` or `osx-x64` architecture. Windows ARM64 hosts also target `win-x64`. Each invocation uses a new directory under the repository's `artifacts/releases/<RID>/<Configuration>/<UTC timestamp>-<unique ID>/`, independent of the current working directory. The directory includes the installer or DMG, application payload, and `package-manifest.json` with package hashes. The publisher prints the complete output path on success.

The entry point accepts the publisher's `-Version`, `-Configuration`, SDK/dependency/license paths, `-SigningIdentity`, `-SkipBuild`, `-NsisPath`, and `-Jobs` options. An explicit `-OutputDirectory` overrides the automatic directory and must be a fresh destination. `-SkipBuild` reuses matching native binaries and still republishes both managed applications and creates the package. Windows requires NSIS 3.11 or newer; dependency/tool checks remain part of the publisher. Installer and DMG builds run on their corresponding operating systems; an incompatible `-RuntimeIdentifier` fails before publishing.

```powershell
pwsh -NoProfile -File ./release.ps1 -Version 1.2.3 -SkipBuild
# Windows with explicit SDK/compiler paths:
pwsh -NoProfile -File ./release.ps1 -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -NsisPath 'C:/Program Files (x86)/NSIS/makensis.exe'
```

## Publishing options

Publishing prints `[publish]` stages after the Workbench build, including each managed publish, native dependency collection, installer/DMG creation, and package hashing. Managed publish, NSIS, signing and DMG commands stream their output while running and retain diagnostics for failures. `Tests were not requested` only describes the preceding build; it does not stop release packaging. Installer compression can take time after compilation has completed.

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

## Windows NSIS installer

The finish page offers an unchecked **Create an AegiNEXT desktop shortcut** option. It creates `AegiNEXT.lnk` targeting `aegi-next.exe`, with the installation directory as its working directory, on the current user's desktop or the shared desktop according to the installation scope. The installer records ownership and the uninstaller removes this link only when the installation created it. Silent installation creates no desktop link by default; add `/DesktopShortcut` to opt in, for example `/S /CurrentUser /DesktopShortcut /D=C:\Apps\AegiNext`.

Add `-CreateInstaller` on Windows to package the complete published `AegiNext/` payload with NSIS 3.11 or newer. The publisher checks the compiler before building, looks for `makensis` on PATH and then in the standard NSIS installation directories, and accepts an explicit `-NsisPath`. Publishing does not install NSIS. `-NsisPath` requires `-CreateInstaller`; macOS rejects `-CreateInstaller`.

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -CreateInstaller -NsisPath 'C:/Program Files (x86)/NSIS/makensis.exe' -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-installer
```

The output contains `AegiNext-0.1.0-win-x64-setup.exe`, `AegiNext/`, and the external `package-manifest.json`. The installer is generated after dependency/license collection and language validation, then included in the final SHA-256 inventory. Only the application payload is embedded; the external manifest is not embedded. `-SkipBuild` still republishes both managed applications and creates the requested installer. Compilation treats NSIS warnings as errors and removes temporary scripts/output on success or failure.

The English/Simplified Chinese wizard lets administrators choose current-user or all-users installation. A fresh installation defaults to the current user under `%LOCALAPPDATA%\Programs\AegiNext`; all-users installation defaults to 64-bit Program Files. Each scope has its own Start Menu shortcuts and uninstall entry, with the selected path remembered for reinstalling. The official [MultiUser](https://nsis.sourceforge.io/Docs/MultiUser/Readme.html) component requests the highest available privileges at startup, so administrator accounts may see UAC even for a current-user installation. Standard accounts without administrator privileges can install for the current user; all-users installation requires running with administrator privileges.

Silent installation accepts `/S /CurrentUser` or `/S /AllUsers`. An optional `/D=` sets the destination and must be last, without quotes around the path; the following PowerShell example passes it as one argument:

```powershell
$installer = (Resolve-Path ./artifacts/releases/windows-installer/AegiNext-0.1.0-win-x64-setup.exe).ProviderPath
$process = Start-Process -FilePath $installer -ArgumentList '/S /CurrentUser /D=C:\Apps\AegiNext' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode)" }
```

Reinstallation, including the development version `0.1.0`, first runs the previous uninstaller in the selected scope to remove obsolete packaged files. Different scopes cannot share one destination. File access checks reject files in use before removing the payload; close both AegiNext and its export worker before updating or uninstalling. Uninstallation deletes the owned file list and empty directories and preserves additional files and `%APPDATA%\AegiNext`. The installed `Uninstall.exe` remembers its actual scope even when both scopes are installed. Automated uninstall callers must use `/S /CurrentUser _?=C:\Apps\AegiNext` (or `/AllUsers`), with the unquoted `_?=` path last, to wait for the actual result and receive a nonzero failure code; normal launch starts a temporary child process. The reinstall flow already uses this synchronous form.

NSIS compilation and package hashing do not establish Windows installation acceptance or Authenticode signing. Validate both scopes, same-version reinstall, obsolete DLL removal, blocked files, paths with spaces/Chinese characters, Start Menu/Settings uninstall entries, installed application/export worker behavior, and preservation of user data on Windows. Native wizard/UAC/icon appearance requires visual acceptance.

## Application icon and DMG

The application icon uses a redesigned leaning bat/cat mascot, a slate-blue rounded tile, a caption slate and a red timing indicator, with no text. The selected generated source, prompt and platform assets live in `src/AegiNext.Desktop/Assets/`. `AppIcon.Source.png` preserves the selected generation; `AppIcon.png` is the 1024×1024 master with a normalized transparent outer contour.

Regenerate the platform assets on macOS using system AppKit, `sips` and `iconutil`:

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

`AppIcon.ico` contains transparent 32-bit representations at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels for the Windows executable, native window icon and taskbar. Shared custom title bars do not display an application icon. `AppIcon.icns` supplies standard and Retina representations from 16 to 1024 pixels. The publisher installs it in `Contents/Resources/` and writes `CFBundleIconFile` before signing the app for Finder and Dock.

Add `-CreateDmg` to macOS publishing to create a compressed disk image containing the signed `AegiNext.app` and an `/Applications` shortcut:

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -CreateDmg -OutputDirectory ./artifacts/releases/mac-icon
```

`AegiNext-0.1.0-osx-arm64.dmg` is generated inside that publish directory, checked with `hdiutil verify` and included in the outer `package-manifest.json` hashes. Temporary staging is removed after success or failure. Windows rejects `-CreateDmg`. The default ad-hoc app signature and disk-image integrity check do not establish notarization; final Finder/Dock and Windows multi-DPI appearance require native visual acceptance.

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
