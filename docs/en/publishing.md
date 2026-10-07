# Publish packages

[English](publishing.md) · [简体中文](../zh-cn/publishing.md) · [All guides](README.md)

## Create a release

Run on the target operating system:

```powershell
pwsh -NoProfile -File ./release.ps1
```

This builds Release and creates a macOS DMG for the host architecture or a Windows x64 NSIS installer. Output goes to a fresh `artifacts/releases/<RID>/<Configuration>/<timestamp>-<ID>/` directory and is printed on success. Windows ARM64 hosts target x64.

```powershell
pwsh -NoProfile -File ./release.ps1 -Version 1.2.3 -SkipBuild
```

Version defaults to `Directory.Build.props`. `-SkipBuild` reuses matching native output and still publishes both managed apps. Use explicit `-FfmpegRoot`, `-SdlRoot`, or `-NsisPath` when needed; output must be a new directory. Publishing does not install dependencies.

## Create a portable package

```powershell
# macOS:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
# Windows:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

Add `-CreateDmg` on macOS or `-CreateInstaller` on Windows. NSIS requires 3.11+; `-NsisPath` requires installer creation. Packaging runs on the corresponding OS.

Install NSIS on Windows with Scoop before creating an installer:

```powershell
scoop bucket add extras
scoop install nsis
```

## Package contents

The complete payload includes self-contained .NET, the desktop app, `aegn-exporter`, FFmpeg/FFprobe, native modules, recursive runtime dependencies, original licenses, and `i18n/en-US.json` plus `i18n/zh-CN.json`.

- `media-runtime.json` selects package-local tools.
- `package-manifest.json` records version, RID, Git identity/dirty state, runtime/tool versions, dependency/license origins, actual OS requirements, and file hashes.
- macOS rewrites Mach-O dependency paths and signs the bundle; the highest native-library requirement becomes the minimum OS version. Default signing is ad-hoc; notarization is separate.
- Windows collects x64 PE dependencies from selected SDK/compiler locations. Use `-RuntimeDependencyDirectory` and `-LicenseDirectory` for explicit additional sources.

## Installers and icons

The Windows wizard supports current-user and all-users scopes; current-user installation defaults to `%LOCALAPPDATA%\Programs\AegiNext`. Desktop shortcut creation is optional. Close the app and worker before reinstalling/uninstalling. Reinstallation removes obsolete owned files; uninstall preserves extra files and personal preferences.

Silent install accepts `/S /CurrentUser` or `/S /AllUsers`; add `/DesktopShortcut` if desired and place an unquoted `/D=...` last. Synchronous automated uninstall uses the matching scope and `_?=...` last.

Regenerate platform icons on macOS:

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

## Verify and relocate

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

Verification checks complete file hashes and language-pack structure/encoding. Move the entire package; macOS copies/archives must retain extended attributes for signing. Use Finder or `ditto`, then verify the moved bundle's signature.

On each target OS, check startup, media opening, audible playback, subtitle editing, actual export/cancel, and relocated operation without developer tools on PATH. Installer scopes, updates, native menus, DPI, and HDR display require their corresponding runtime/visual checks.
