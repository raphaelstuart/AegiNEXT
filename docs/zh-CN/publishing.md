# AegiNext 平台发布

[English](../publishing.md) | [简体中文](publishing.md)

开发默认版本为 `Directory.Build.props` 中的 `0.1.0`。发布时通过 `publish.ps1 -Version 1.2.3` 指定版本，无需修改源码；使用三段数字，每段范围为 0–65535。发布器将同一版本写入主程序与 ExportWorker 的程序集元数据、`package-manifest.json`、macOS bundle／DMG 及 Windows 安装包。**帮助 → 关于** 从程序集读取应用名称与产品版本，并显示当前软件图标、当前年份版权及 GPLv3 声明。构建身份、文件哈希与时间放在 `package-manifest.json`。发布包含自包含的 .NET 10、主程序、独立 ExportWorker、FFmpeg/ffprobe、自有媒体模块及第三方动态依赖。

## 自动生成发行包

两种受支持的平台都运行同一入口：

```powershell
pwsh -NoProfile -File ./release.ps1
```

`release.ps1` 默认使用 `Release` 配置构建，并自动为 publish 选择当前平台的打包选项：Windows 生成 `win-x64` 的 NSIS 安装包；macOS 按宿主架构生成 `osx-arm64` 或 `osx-x64` 的 DMG。Windows ARM64 宿主同样生成 `win-x64`。每次调用都使用仓库内的新目录 `artifacts/releases/<RID>/<Configuration>/<UTC 时间戳>-<唯一标识>/`，不受当前工作目录影响。目录内包含安装包或 DMG、应用目录以及含文件哈希的 `package-manifest.json`；成功后会打印完整输出路径。

入口接受 publish 的 `-Version`、`-Configuration`、SDK／依赖／许可证路径、`-SigningIdentity`、`-SkipBuild`、`-NsisPath` 和 `-Jobs` 参数。显式传入 `-OutputDirectory` 可覆盖自动目录，目标仍须是新目录。`-SkipBuild` 复用匹配的 native 产物，仍会重新 publish 两个 managed 程序并生成安装包或镜像。Windows 需要 NSIS 3.11 或更新版本；工具和依赖检查继续由发布器完成。安装包和 DMG 分别在对应操作系统生成，传入不匹配的 `-RuntimeIdentifier` 会在发布前失败。

```powershell
pwsh -NoProfile -File ./release.ps1 -Version 1.2.3 -SkipBuild
# Windows 显式指定 SDK 和 NSIS 编译器：
pwsh -NoProfile -File ./release.ps1 -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -NsisPath 'C:/Program Files (x86)/NSIS/makensis.exe'
```

## 手动选择发布参数

Workbench 构建结束后，发布器会打印 `[publish]` 阶段提示，分别标明 managed publish、native 依赖收集、安装包／DMG 生成和文件哈希校验。managed publish、NSIS、签名及 DMG 命令运行时实时显示输出，并保留失败诊断。`Tests were not requested` 只说明前面的构建没有要求运行测试，不会停止发行打包；编译完成后，安装包压缩仍可能需要一段时间。

macOS 在对应架构 Mac 构建：

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -OutputDirectory ./artifacts/releases/mac-local
```

Windows 在 Windows 本机以 .NET SDK、x64 MinGW、FFmpeg shared 开发 SDK 和 SDL3 SDK 构建；允许 Parallels ARM64 SDK 宿主显式生成并执行 x64 产物：

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local
```

输出目录必须是新目录，避免覆盖旧发布。`-SkipBuild` 复用当前 RID/Configuration 的 native 产物，仍重新 publish managed 主程序与 worker，并检查媒体 SDK。发布器不安装或替换依赖；普通 `build.ps1 -RuntimeIdentifier win-x64` 的还原和构建也使用同一 RID。Windows 默认目标是 win-x64，RID 的 `obj` 独立，防止共享仓库混用 Mac/ARM64 的 NuGet 资产。

`build.ps1 -RunTests -TestProjects Media` 在方案构建之后独立构建并执行所选测试项目，保留 `--no-restore` 和对应 RID。默认 `Release|Any CPU` 方案配置没有启用全部测试项目的 Build，因此不会用 `--no-build` 假定测试 DLL 已生成。

NuGet 无 RID 开发还原继续使用 `packages.lock.json`；RID 构建分别使用稳定的 `packages.osx-arm64.lock.json`、`packages.osx-x64.lock.json`、`packages.win-x64.lock.json`。restore、build、test 和两个 publish 都显式传 `-p:AegiNextRuntimeIdentifier=<RID>`，让 `Directory.Build.props` 早期就选择同一锁和中间目录；restore 的 `-r` 本身可能只设置复数 `RuntimeIdentifiers`，无法保证这一点。

首次维护或依赖变化时，在 Mac/Linux 宿主使用 `dotnet restore AegiNext.sln --force-evaluate` 维护通用锁，三个 RID 各自使用 `dotnet restore AegiNext.sln -r <RID> -p:AegiNextRuntimeIdentifier=<RID> --force-evaluate`，审查并保留全部锁文件。Windows 默认设置 win-x64；维护通用锁时要显式传空的 `-p:RuntimeIdentifier=`。正常构建及发布仍使用 locked mode，不自动接受已有锁的依赖变化。

macOS 发布器扫描实际 Mach-O 依赖和最低系统版本，复制非系统递归依赖至 `Contents/Frameworks`，改写相对加载路径，再签名验证。`-SigningIdentity` 默认 `-` 为本地 ad-hoc，可指定实际签名身份；公证由发行流程完成。包的 `LSMinimumSystemVersion` 是所有已收集二进制的真实最大要求，不以 CMake 的 14.0 目标代替验证。本机 Homebrew 依赖当前可能要求 macOS 27，发布器不会声称支持 macOS 14。

Windows 使用 MinGW `objdump` 扫描 x64 PE 依赖，从所选 SDK/native 目录及已经验证为 x64 的 `g++` 所在目录补齐非系统 DLL。编译器路径按符号链接解析，使用对应前缀的原始 `licenses`，不搜索整个 PATH。工具目录也具有邻接 DLL，避免依赖 PATH。系统 API Set 与 Windows 系统 DLL由系统提供，第三方 VC/MinGW 运行库须进入闭包。

Windows 系统依赖识别包含实测 FFmpeg/Skia 导入的 [Ncrypt.dll](https://learn.microsoft.com/en-us/windows/win32/api/ncrypt/nf-ncrypt-ncryptopenstorageprovider)、[Avicap32.dll](https://learn.microsoft.com/en-us/windows/win32/api/vfw/nf-vfw-capcreatecapturewindowa) 和 [FontSub.dll](https://learn.microsoft.com/en-us/windows/win32/api/fontsub/nf-fontsub-createfontpackage)，它们由 Windows 提供。

第三方依赖不在已选 SDK 目录时，可通过 `-RuntimeDependencyDirectory` 显式提供其运行库目录；发布器不会搜索或复制开发机的系统安装目录。该目录也需要原始 license notices，可用 `-LicenseDirectory` 对应子目录补齐。

应用内 `media-runtime.json` 标记完整媒体发布模式，运行时使用包内 `tools/ffmpeg` 和 `tools/ffprobe`；缺失时明确失败，不回退开发机工具。未标记的开发运行可继续用 `AEGINEXT_FFMPEG_PATH`、`AEGINEXT_FFPROBE_PATH` 或 PATH。

发布器复制原始第三方 LICENSE/COPYING/NOTICE 文件并记录来源及哈希；native 包缺少原始 notices 时停止。可通过 `-LicenseDirectory` 提供缺失包的 notices 子目录。完整 `package-manifest.json` 位于包外，记录 Git SHA／工作区是否有改动、包内实际执行的 FFmpeg／ffprobe 版本首行、两个应用的 `includedFrameworks`，并包含签名后所有文件的哈希；它不包含自身的递归哈希。Git 无法读取时标记不可用，SHA 和 dirty 为 null，不伪报干净构建。包内工具版本在 native 闭包与 macOS 签名完成后读取。

Windows 清单的 `OperatingSystemPolicy.RequiredRuntimePolicy` 按 [.NET 10 官方系统支持政策](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) 记录目前受支持的 Windows 客户端版本、生命周期限制和 Windows 11 ARM64 的 x64 模拟支持。该政策于 2026-10-04 核对；构建机器说明不等于测试结果，完整 native 应用的最低 Windows 版本仍需对应实机验证。发行验收需在干净 PATH、未安装 .NET/FFmpeg 的目标机器验证媒体打开、播放、音频、字幕合成、实际 worker 压制与取消，并分别记录 Windows 与 macOS 的实机结果。

## Windows NSIS 安装包

安装完成页提供默认不勾选的 **创建 AegiNEXT 桌面快捷方式** 选项。勾选后生成 `AegiNEXT.lnk`，目标为 `aegi-next.exe`，工作目录为安装目录；根据安装范围放到当前用户桌面或公共桌面。安装器记录是否创建过该链接，卸载时仅在本次安装拥有它的情况下清理。静默安装默认不创建桌面链接；可添加 `/DesktopShortcut`，例如 `/S /CurrentUser /DesktopShortcut /D=C:\Apps\AegiNext`。

Windows 发布增加 `-CreateInstaller` 后，会使用 NSIS 3.11 或更新版本封装完整的 `AegiNext/` 发布目录。发布器在构建前检查编译器，先查找 PATH 中的 `makensis`，再检查标准 NSIS 安装目录；也可通过 `-NsisPath` 指定。发布器不安装 NSIS。`-NsisPath` 需要配合 `-CreateInstaller`；macOS 不接受 `-CreateInstaller`。

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -Configuration Release -CreateInstaller -NsisPath 'C:/Program Files (x86)/NSIS/makensis.exe' -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-installer
```

输出包含 `AegiNext-0.1.0-win-x64-setup.exe`、`AegiNext/` 和外层 `package-manifest.json`。安装器在依赖、许可证收集及语言资源校验后生成，随后进入最终 SHA-256 清单；安装器仅嵌入应用目录，避免与外层清单产生哈希循环。`-SkipBuild` 仍会重新 publish 两个 managed 程序并生成指定安装器。NSIS 编译警告按错误处理，临时脚本和编译输出在成功或失败后清理。

安装向导支持英文／简体中文，管理员可以选择“当前用户”或“所有用户”。首次默认当前用户，目录为 `%LOCALAPPDATA%\Programs\AegiNext`；所有用户默认使用 64 位 Program Files。两个范围分别拥有开始菜单快捷方式、卸载项，并记忆重装路径。使用官方 [MultiUser](https://nsis.sourceforge.io/Docs/MultiUser/Readme.html) 组件的最高可用权限声明，管理员账户启动时可能出现 UAC，即使随后选择当前用户；普通账户未获得管理员权限时只能按当前用户安装，所有用户安装需要以管理员权限运行。

静默安装支持 `/S /CurrentUser`、`/S /AllUsers`。可选 `/D=` 指定目录，必须最后传入，目录值本身不加引号；下面的 PowerShell 示例将整段命令行作为一个参数传递：

```powershell
$installer = (Resolve-Path ./artifacts/releases/windows-installer/AegiNext-0.1.0-win-x64-setup.exe).ProviderPath
$process = Start-Process -FilePath $installer -ArgumentList '/S /CurrentUser /D=C:\Apps\AegiNext' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installation failed: $($process.ExitCode)" }
```

重装（包括同一开发版本 `0.1.0`）会先调用所选范围的旧卸载器，清理不再发布的文件。不同安装范围不能共用一个目标目录。移除应用文件前检查文件是否被占用；更新或卸载前需关闭主程序和 ExportWorker。卸载只删除记录中的文件及空目录，保留额外文件和 `%APPDATA%\AegiNext`；两种范围并存时，直接运行安装目录中的 `Uninstall.exe` 也能恢复本次安装的真实范围。自动化调用卸载器时，需要使用 `/S /CurrentUser _?=C:\Apps\AegiNext`（或 `/AllUsers`），最后传入不加引号的 `_?=` 目录，才能同步等待真实结果并读取非零失败码；普通启动会另起临时子进程。重装流程已使用同步形式。

NSIS 编译和包哈希校验不代表 Windows 安装验收或 Authenticode 签名。Windows 验收需覆盖两种范围、同版重装、旧 DLL 清理、文件占用、中文／空格路径、开始菜单及设置中的卸载项、安装后主程序／worker 运行和用户文件保留。原生向导、UAC 和图标观感需视觉验收。

## 应用图标与 DMG

桌面图标采用经过重新设计姿态的黑色蝠翼角色、雾蓝圆角底板、字幕卡片及红色时间指示。图标不含文字。设计源、生成提示与平台资产位于 `src/AegiNext.Desktop/Assets/`；`AppIcon.Source.png` 是选定的生成源，`AppIcon.png` 是经过透明外缘规范化的 1024×1024 母版。

在 macOS 上可使用系统 AppKit、`sips` 和 `iconutil` 重新导出，无需安装图像处理依赖：

```powershell
pwsh -NoProfile -File ./scripts/assets/export-app-icons.ps1 -SourcePng ./src/AegiNext.Desktop/Assets/AppIcon.Source.png
```

`AppIcon.ico` 包含 16、20、24、32、40、48、64、128、256px 的透明 32-bit 表示，用于 Windows 可执行文件、窗口系统图标及任务栏。共享自绘标题栏不显示应用图标。`AppIcon.icns` 覆盖 macOS 16–1024px 的标准及 Retina 表示。发布器在签名前将 ICNS 复制到 `Contents/Resources/` 并写入 `CFBundleIconFile`，供 Finder 和 Dock 使用。

macOS 发布可添加 `-CreateDmg`，生成包含已签名 `AegiNext.app` 与 `/Applications` 快捷入口的压缩镜像：

```powershell
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -Configuration Release -CreateDmg -OutputDirectory ./artifacts/releases/mac-icon
```

`AegiNext-0.1.0-osx-arm64.dmg` 位于该发布目录内，经过 `hdiutil verify` 并纳入外层 `package-manifest.json` 哈希清单。临时镜像目录在成功或失败后清理；Windows 不接受 `-CreateDmg`。App 的默认 ad-hoc 签名和镜像完整性检查仍不表示已完成公证。Finder/Dock 及 Windows 不同 DPI 下的最终图标观感需要原生视觉验收。

## 语言资源

桌面项目将 `src/AegiNext.Desktop/I18n/Languages/` 中无 BOM 的 UTF-8 JSON 自动复制至构建、测试和发布输出的 `AppContext.BaseDirectory/i18n/`。macOS 包内位置为 `AegiNext.app/Contents/MacOS/i18n/`，Windows 为 `AegiNext/i18n/`。必须包含 `en-US.json` 和 `zh-CN.json`，每个根对象提供非空 `LanguageName`、对应有效文化标识 `LanguageID` 及字符串值字典 `Strings`。额外语言包的文件名可以不同于 ID，但目录第一层所有 JSON 的 ID 必须唯一。应用启动时读取，修改或新增后重启生效。统一接口、XAML 注入和语言匹配规则见[工作区本地化说明](composable-workspace.md#本地化)。

`verify-package.ps1` 在原有完整文件哈希校验之外，检查包内语言目录、两份内置文件、UTF-8 编码、JSON 形状、元信息、字符串条目和重复 ID／key。语言 JSON 继续纳入已有哈希清单；修改包内文案后须通过正常发布／签名流程更新清单才能通过包校验。这些资源检查不代表已完成翻译控件和平台菜单的原生视觉验收。

移动完整发布目录后可只读校验哈希，任何缺失、额外或修改的文件都会报错：

```powershell
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

macOS 的非 Mach-O .NET 程序集签名包含扩展属性，搬移或归档也需要保留。使用 Finder 或系统 `ditto`；普通逐文件复制仅保证文件内容哈希一致，可能丢失签名元数据。开发 ZIP 可这样生成和解压后验证：

```sh
ditto -c -k --sequesterRsrc --keepParent artifacts/releases/mac-local artifacts/releases/mac-local.zip
ditto -x -k artifacts/releases/mac-local.zip '/path/with spaces/moved'
codesign --verify --deep --strict '/path/with spaces/moved/mac-local/AegiNext.app'
```

两平台 0.1.0 开发包的实际搬移、清除开发 PATH、解码、音频、压制、取消及工作台验收见[交互与发布 Checkpoint](README.md#实施与验收记录)。

本轮字幕优化、DSL 编辑器与向量动画的更新包及独立 worker 验收另见[字幕编辑 Checkpoint](README.md#实施与验收记录)。

完整颜色动画、HEX 输入与关键帧交互更新包见[颜色工作区 Checkpoint](README.md#实施与验收记录)。
