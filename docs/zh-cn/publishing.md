# 发布打包

[English](../en/publishing.md) · [简体中文](publishing.md) · [全部指南](README.md)

## 生成发布包

在目标操作系统运行：

```powershell
pwsh -NoProfile -File ./release.ps1
```

脚本构建 Release，macOS 生成宿主架构的 DMG，Windows 生成 x64 NSIS 安装包。输出使用全新的 `artifacts/releases/<RID>/<Configuration>/<timestamp>-<ID>/` 目录，成功后打印完整路径。Windows ARM64 宿主仍以 x64 为目标。

```powershell
pwsh -NoProfile -File ./release.ps1 -Version 1.2.3 -SkipBuild
```

版本默认来自 `Directory.Build.props`。`-SkipBuild` 复用匹配原生产物，仍发布两个托管应用。必要时指定 `-FfmpegRoot`、`-SdlRoot` 或 `-NsisPath`；输出目录必须尚不存在，发布不会安装依赖。

## 生成便携包

```powershell
# macOS:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
# Windows:
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

macOS 添加 `-CreateDmg`，Windows 添加 `-CreateInstaller`。NSIS 要求 3.11+，`-NsisPath` 必须与安装包生成配合；打包须在对应系统执行。

Windows 生成安装包前，通过 Scoop 安装 NSIS：

```powershell
scoop bucket add extras
scoop install nsis
```

## 应用包内容

完整内容包括自包含 .NET、桌面应用、`aegn-exporter`、FFmpeg/FFprobe、原生模块、递归运行时依赖、原始许可证，以及从发布输出整体复制的 `i18n/` 语言资源目录。

- `media-runtime.json` 指定包内工具。
- `package-manifest.json` 记录版本、RID、Git 身份/脏状态、运行时/工具版本、依赖/许可来源、实际系统要求和文件哈希。
- macOS 重写 Mach-O 依赖路径并签名，最高原生库要求决定最低系统版本；默认临时签名，公证另行处理。
- Windows 从选定 SDK/编译器位置收集 x64 PE 依赖；额外来源使用 `-RuntimeDependencyDirectory` 和 `-LicenseDirectory`。

## 安装器与图标

Windows 向导支持当前用户和所有用户，当前用户默认安装到 `%LOCALAPPDATA%\Programs\AegiNext`；桌面快捷方式可选。更新/卸载前关闭应用及 worker。重装清理过期的自有文件，卸载保留额外文件和个人设置。

静默安装接受 `/S /CurrentUser` 或 `/S /AllUsers`，需要快捷方式时加 `/DesktopShortcut`，不带引号的 `/D=...` 放最后。自动同步卸载使用对应范围，且 `_?=...` 放最后。

macOS 重新生成平台图标：

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

## 校验与搬移

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

校验覆盖完整文件哈希及语言包结构/编码。搬移整个应用包；macOS 拷贝/归档须保留签名所需的扩展属性，使用 Finder 或 `ditto`，再校验搬移后的签名。

分别在目标系统检查启动、媒体打开、可听播放、字幕编辑、实际导出/取消，以及无开发工具 PATH 的搬移运行。安装范围、更新、原生菜单、DPI 和 HDR 显示需要对应实机/视觉检查。
