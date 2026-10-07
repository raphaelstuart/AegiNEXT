# 构建与运行

[English](../en/building.md) · [简体中文](building.md) · [全部指南](README.md)

## 配置环境

使用 .NET SDK **10.0.401**（允许同 feature band 的稳定补丁）和 **PowerShell 7.2+**。原生工作台支持 macOS `osx-arm64`/`osx-x64`、Windows `win-x64`；Windows ARM64 宿主仍以 x64 为目标。Linux 原生媒体与运行验收暂未支持。

原生构建需要 CMake、Ninja、编译器、共享 FFmpeg SDK 和 SDL3。macOS 使用 Homebrew 与 Apple SDK，Windows 使用 Scoop 和 x64 MinGW。版本由 `ffmpeg-toolchain.json` 及原生依赖清单定义，只有命令行工具的 FFmpeg 包不够；当前媒体锁定 FFmpeg 9.0.2、SDL3 3.4.16。

在仓库根目录执行：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

缺少已识别的软件包时，显式使用 `-InstallDependencies`。环境检查不安装、不还原、不构建；已有错误版本不会自动替换。macOS 最低版本取决于实际收集的原生库，不能仅看工程部署目标。

## 启动

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

Windows 将两个 RID 换成 `win-x64`，Intel Mac 换成 `osx-x64`。Windows 显式构建：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release
```

## Rider 调试

打开 `AegiNext.sln`。首次 Debug 调试前或修改 native/ABI 后，构建匹配的原生库：

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows:
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

随后以 Debug 运行 `AegiNext.Desktop`。普通 dotnet/Rider 构建只复制已有产物，不编译缺失的原生库；Debug/Release 相互隔离，重建后停止旧进程。压制错误 ANX1001/ANX1002 表示原生契约元数据缺失/过期，重建对应 Workbench。

## 构建选项

| 目标 / 参数 | 用途 |
|---|---|
| `Managed`，默认 | 构建托管项目并复制已有原生产物 |
| `Workbench` | Decoder → Audio → Export → Managed |
| `Decoder`、`Audio`、`Export` | 单独构建对应原生模块 |
| `Native`、`All` | 可选 macOS HDR 诊断；All 加 Managed，不含 Workbench |
| `-FfmpegRoot`、`-SdlRoot` | 显式共享 SDK 路径 |
| `-WithMediaTools` | 托管构建时检查 PATH FFmpeg/FFprobe |
| `-ReportPath` | 保存环境报告 |

FFmpeg SDK 按显式路径 → `FFMPEG_DIR` → 包管理器前缀解析；SDL 按显式路径 → `SDL3_DIR` → 包管理器前缀解析。显式路径无效即失败。开发工具使用绝对 `AEGINEXT_FFMPEG_PATH`/`AEGINEXT_FFPROBE_PATH` 或 PATH，完整应用包使用包内工具。

原生产物位于 `artifacts/native/<RID>/<Configuration>/`，托管中间文件使用 `obj/<RID>`。NuGet 版本集中在 `Directory.Packages.props`，普通还原且不维护 lock 文件。Release 构建使用 `AegiNext.Product.slnf`，Debug 使用 `AegiNext.sln`。

## 定向测试

```powershell
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~EffectScript'
```

项目筛选支持 Core、Application、Rendering、Media、Desktop、Desktop.Ui。未指定 `-RunTests` 不运行测试；Workbench 测试也执行三组原生 CTest。Release 仅还原选中的测试项目。

真实媒体/设备测试需要匹配原生产物、实际工具路径和显式开关：`AEGINEXT_RUN_DECODER_TESTS=1`、`AEGINEXT_RUN_AUDIO_TESTS=1`；系统设备额外需要 `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`。否则原生用例跳过，详见[媒体](media.md)。

构建脚本 QA：

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

首条命令将固定版本 QA 工具还原到工程产物目录。Headless UI 和静音音频测试不能证明原生外观或可听延迟；应用打包见[发布](publishing.md)。
