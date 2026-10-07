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

项目筛选支持 Core、Application、Rendering、Media、Desktop、Desktop.Ui。未指定 `-RunTests` 不运行测试；Workbench 测试也执行对应的原生 CTest 套件。Release 仅还原选中的测试项目。

真实媒体/设备测试需要匹配原生产物、实际工具路径和显式开关：`AEGINEXT_RUN_DECODER_TESTS=1`、`AEGINEXT_RUN_AUDIO_TESTS=1`；系统设备额外需要 `AEGINEXT_RUN_SYSTEM_AUDIO_TESTS=1`。否则原生用例跳过，详见[媒体](media.md)。

构建脚本 QA：

```powershell
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools
pwsh -NoProfile -File ./scripts/test-build.ps1
```

首条命令将固定版本 QA 工具还原到工程产物目录。Headless UI 和静音音频测试不能证明原生外观或可听延迟；应用打包见[发布](publishing.md)。

## 压制性能计时

启动 `aegn-exporter` 前设置 `AEGINEXT_EXPORT_PROFILE=1`，原生导出成功后会向 stderr 写入以 `AEGINEXT_EXPORT_PROFILE` 开头的 JSON，记录帧数、原生总耗时、解码/下载、渲染、前景范围构建、YUV 重采样、合成、编码和封装耗时。前景计数区分更新、版本复用、有效像素和有界颜色预计算。配套的 `AEGINEXT_RENDER_PROFILE` 分别记录求值、绘制、F32 复制耗时，以及复制字节数和临时表面的分配／复用次数。正常 worker 协议仍使用 stdout；未设置开关时不读取阶段时钟。

诊断时直接捕获 worker stderr，桌面导出器会消费该输出。另行测量包含 worker 启动和最终封装的完整导出时间，比较 FPS 时使用相同素材、工程快照、配置和构建模式。内部重采样与合成每阶段最多使用四个线程；GPU 编码仍可能受 CPU 合成速度限制。

导出 ABI 5 显式报告字幕前景已更新、未变化或为空。原生导出保留 F32 缓冲，并为稳定版本复用预计算的有效范围。连续更新时直接使用原有合成内核，省去有效范围构建；首次恢复为未变化状态时再构建一次。视频背景仍执行相同的全帧 YUV 重采样。ABI 变化后必须一起重新构建应用和 worker。

首帧确认 VideoToolbox 硬件后端后，自动启用一帧解码预取；软件及其他后端默认串行。设置 `AEGINEXT_EXPORT_PREFETCH=0` 可关闭预取，`1` 可在任何后端强制启用；其他显式值也使用串行解码。生产线程独占解码，并在发布前固定帧的颜色信息；渲染与编码仍在导出线程执行。`read_ms` 表示解码服务耗时，可能与后续阶段重叠；`prefetch_wait_ms` 表示导出线程的实际等待耗时。比较完整导出时间，不要累加重叠阶段的时长。诊断计数包含队列容量和排队／读取中的峰值帧数。

macOS 原生构建启用 `BUILD_TESTING=ON` 后还会生成 `aeginext_export_metal_tests`。使用 `--benchmark 8` 测量实验性 SDR Metal 合成，计入上传、同步、回读及相同的 CPU 重采样。这个独立研究目标不会启用常规导出的 GPU 合成；它报告与 CPU 基线的数值差异，任何差异都不满足默认逐像素一致的启用要求。HDR 及解码／编码 GPU 表面的互操作尚不在原型范围内。
