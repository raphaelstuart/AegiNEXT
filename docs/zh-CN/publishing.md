# AegiNext 平台发布

[English](../publishing.md) | [简体中文](publishing.md)

开发中的应用版本固定为 `0.1.0`；构建身份、文件哈希与时间放在 `package-manifest.json`。发布包含自包含的 .NET 10、主程序、独立 ExportWorker、FFmpeg/ffprobe、自有媒体模块及第三方动态依赖。

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
