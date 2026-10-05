# AegiNext

跨平台字幕打轴、可视化特效与视频压制软件。

第一次使用请阅读 [Quick Start · 用户快速开始](docs/quick-start.md)。使用手册与开发说明集中在 [docs 文档索引](docs/README.md)，分阶段实施及验收记录位于 [plans](plans/README.md)。

当前为 **开发预览**：已贯通影音播放、逐行字幕编辑、快捷键打轴、波形／语谱图时间线、工程保存与撤销重做、可视化关键帧／路径／蒙版／分组合成，以及独立进程视频压制。支持字体、字号、颜色、描边、逐字高亮、可复用特效预设与可迁移字幕样式预设；提供标准菜单、切页设置窗口、可自定义应用内快捷键、深浅色及主色、中文／英文界面。

工作区支持七个固定面板的停靠、浮动、隐藏和命名布局；顶部“布局”与“视图”菜单提供切换入口。字幕采用多个有名称的轨道，同轨 Clip 不允许重叠；时间线上方概览条与触控板／滚轮管理视口。选中 Clip 或关键帧后，在视频预览中操作画面，在特效面板编辑对应属性。字幕按文字自然伸展，位置控件提供九宫格 Anchor、Pivot 和像素偏移；日志为独立可停靠面板。工程读写基线为 **v3**，明确拒绝 v1／v2 工程；个人布局和旧快捷键独立迁移。行为、自动证据及人工验收边界见 [多轨工作区实施记录](plans/multitrack-workspace-checkpoints.md)。

编辑预览统一显示 SDR；PQ／HLG 压制从原始高精度视频帧合成并输出 HEVC 10-bit，保留 HDR 高光与受支持的色彩信息。0.1.0 开发发布包已进行 macOS 与 Parallels Windows 11 的媒体打开、音频播放、实际压制、取消及搬移验证；触控板、多屏和跨 DPI 等复杂交互的验收边界见 [交互与发布记录](plans/interaction-release-checkpoints.md)。使用方法见 [工作台](docs/workbench.md)和 [导出](docs/export.md)。

## 开发环境

- .NET SDK `10.0.401`，允许同一 feature band 的稳定补丁更新。
- Avalonia `12.1.3`；依赖版本集中于 `Directory.Packages.props`，传递依赖由各项目的 `packages.lock.json` 锁定。
- SkiaSharp / SkiaSharp.HarfBuzz `3.119.4`；HarfBuzzSharp 及已使用平台的原生包通过集中传递依赖锁定统一为 `8.3.1.5`。
- 开发发布目标：Apple Silicon macOS、Windows x64；本机当前 macOS 包要求 27.0，具体包的最低版本由依赖闭包生成。构建与发布条件见 [平台发布说明](docs/publishing.md)。
- PowerShell 7.2+。macOS 使用 Homebrew，Windows 使用 Scoop；缺少包管理器时脚本提供引导，不自动执行远程安装脚本。

在仓库根目录执行：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
pwsh -NoProfile -File ./build.ps1 -Target Workbench -InstallDependencies
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application,Desktop
```

`Workbench` 检查并顺序构建 Decoder、Audio、Export 和 Managed，生成完整开发工作台。未指定目标时仍为 `Managed / Release`；普通构建只检查环境，缺项返回非零退出码，只有显式 `-InstallDependencies` 才安装缺失依赖。`-RunTests` 可按项目筛选测试。完整参数、固定 SDK 版本、依赖检查和平台边界见 [构建说明](docs/building.md)。

构建后按相同 RID 启动，例如 Apple Silicon：

```powershell
dotnet run --project src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --runtime osx-arm64 -p:AegiNextRuntimeIdentifier=osx-arm64 --no-build
```

Windows 使用 `win-x64` 替换两个 RID 参数。关闭主窗口即退出进程。也可以在 Rider 中打开 `AegiNext.sln`，选择 `AegiNext.Desktop` 启动。

影音播放和压制使用独立 FFmpeg／SDL3 原生库。开发运行时可将工具放在 PATH，或设置 `AEGINEXT_FFPROBE_PATH`、`AEGINEXT_FFMPEG_PATH` 为绝对路径。显式路径无效时报告错误。完整发布包包含自有 native 模块、递归运行库依赖和包内 FFmpeg／FFprobe，使用包内工具；打包入口与验证步骤见 [平台发布说明](docs/publishing.md)。可选 HDR 诊断使用 `-Target All`，该目标不包含 Workbench；现有 HDR 显示后端仅支持 macOS。

## 结构

- `src/AegiNext.Core`：纯 C# 领域层；有理时间、媒体事实、自有工程、字幕／图层／动画／蒙版／路径模型与严格验证。
- `src/AegiNext.Application`：编辑命令、撤销重做、裁剪／拉伸／拆分／合并、原子工程保存、资源导入／迁移、SRT／TXT 适配。
- `src/AegiNext.Desktop`：Avalonia 字幕工作台、播放控制器、时间线与画布编辑、主题／语言／音量设置、独立 HDR 诊断窗口。
- `src/AegiNext.Rendering`：不依赖 Avalonia 的 Skia/HarfBuzz 适配层，提供工程求值、文字／图形／图片、线性 F16 蒙版与复杂合成；见 [渲染契约](docs/rendering.md)。
- `src/AegiNext.ExportWorker`：独立导出进程，读取工程快照，复用场景渲染，保持源时间戳和受支持的 HDR 信号。
- `Tests/AegiNext.Core.Tests`：xUnit 行为测试。
- `Tests/AegiNext.Rendering.Tests`：实际调用 Skia/HarfBuzz 原生库的离屏测试，包含锁定字节与授权文件的测试字体。
- `src/AegiNext.Media`：F16 帧边界校验、C ABI 绑定、原生会话／帧所有权、独立 FFprobe 探测、软件解码／精确定位、播放会话与独立 SDR 显示派生；见 [媒体探测](docs/media-probing.md)、[视频解码](docs/video-decoding.md)、[播放基础](docs/video-playback.md)和[SDR 预览](docs/video-preview.md)。
- `Tests/AegiNext.Media.Tests`：帧输入、ABI、探测解析、子进程行为及显式启用的真实媒体测试；`AegiNext.Media.TestHost` 为测试专用子进程。
- `Tests/AegiNext.Desktop.Tests`：不依赖窗口系统的预览控制器生命周期、迟到画面和关闭测试。
- `native/`：C++ / Objective-C++ 原生 HDR 实现与 C ABI，使用锁定版本的 libplacebo、MoltenVK；包含原生契约测试。
- `native/decoder`：独立 FFmpeg 软件解码与 CPU SDR 转换 C ABI 和契约测试，不依赖 HDR 显示后端。
- `native/audio`：FFmpeg 音频解码／重采样与 SDL3 输出；`Media/Analysis` 使用独立只读解码生成波形与语谱图。
- `native/export`：独立 SDR／PQ／HLG 高精度合成与编码 C ABI、色彩数值和真实文件回读测试。
- `build.ps1`、`scripts/build/`：跨平台环境检测、显式安装、构建和测试编排；`Tests/Build` 覆盖脚本行为。
- `docs/architecture.md`：已确定的模块边界、产品范围和 HDR 路线。
- `docs/quick-start.md`、`docs/README.md`：用户快速开始和使用／开发文档索引。
- `plans/checkpoints.md`：分步验收记录与下一步范围。

## 工程约定

- C# 使用 Allman 花括号、文件级命名空间、单文件单类型，优先 `var` 与目标类型 `new()`。
- 开启可空检查、.NET 推荐分析器和警告视为错误。枚举常量按项目规则使用 `ALL_UPPER`；仅在对应枚举文件中覆盖冲突的 CA1707 命名建议。
- 编译产物、测试结果与本地验证截图放在已忽略目录中，依赖锁文件纳入版本管理。
- 阶段开始前明确 Checkpoint，记录定向测试和实机验收边界。本轮按用户“继续执行全部”连续实施，见 [实施记录](plans/continuous-implementation.md)。

## Git 提交规范

参考 Nano Life 的提交习惯，标题采用 `type: summary`，冒号后保留一个空格，使用简短英文说明实际变更。功能提交使用 `feat`，缺陷修复使用 `fix`，纯维护使用 `chore`；例如：

```text
feat: modernize workbench menus, settings and style presets
fix: restore keyframe selection and parameter editing
```

- 一次提交围绕明确的功能或修复，提交前检查暂存范围，不混入无关改动。需要说明原因、验证或限制时，在空行后补充正文。
- 源码、文档、对应测试和依赖锁文件需要提交。AegiNext 的 `.sln` 与 `.csproj` 是手工维护的工程入口，需要纳入版本管理；Nano Life 的 Unity 自动生成工程文件规则不适用于这些文件。
- 不提交 `bin/`、`obj/`、`TestResults/`、`testResults.xml`、`artifacts/`、IDE 配置或本地截图，生成结果保留在本地并由 `.gitignore` 排除。
- 提交前执行 `git diff --check` 和受影响的构建／测试；验收记录区分自动测试、实际运行及尚未验证的平台。
