# 跨平台构建

[English](../building.md) | [简体中文](building.md)

## 入口与目标

项目级入口是根目录 `build.ps1`，公共逻辑位于 `scripts/build/AegiNext.Build.psm1`。从任意目录调用都以脚本所在仓库解析 `global.json` 和解决方案；路径与参数使用独立数组传递，不拼接可执行命令字符串。

需要 PowerShell 7.2+。已有包管理器时：

```powershell
# Windows：从 Windows PowerShell 安装，再使用 pwsh 运行本项目脚本。
scoop install main/pwsh

# macOS
brew install powershell
```

包管理器首次安装见 [Scoop 官方说明](https://scoop.sh/) 或 [Homebrew 官方说明](https://brew.sh/)。脚本不改变执行策略，不自动下载执行包管理器引导代码，也不安装 Apple / Microsoft SDK 安装器。

| 平台 | 默认 Managed | Native / All | Decoder | 自动安装 |
| --- | --- | --- | --- | --- |
| macOS | .NET 解决方案 | 可选 HDR 诊断库；All 先原生后托管 | 独立 FFmpeg 软件解码与 CPU SDR 预览库 | Homebrew |
| Windows x64（支持 ARM64 宿主生成 x64） | .NET 解决方案，统一 win-x64 目标 | HDR 后端尚未实现，明确失败 | 独立 FFmpeg 软件解码与 CPU SDR 预览库；已在 Parallels Windows 11 验证 | Scoop |
| Linux | 托管构建入口，产品运行未验收 | 后端尚未实现，明确失败 | 本步延期，明确失败 | 不自动选择发行版包管理器 |

默认 `Managed / Release`。这不加载或验证 HDR 预览，也不要求 CMake、MoltenVK 或 libplacebo。普通托管编译不依赖媒体工具；`-WithMediaTools` 检查 PATH 上的 `ffprobe` / `ffmpeg`。独立 `Decoder` 目标始终验证所选开发包内的工具和库。现有 HDR native 使用 AppKit / Objective-C++ / CAMetalLayer，只能在 macOS 构建；脚本不会把 Windows Native 自动改成 Managed 并报告成功。

## 常用命令

### Debug／Release 原生压制兼容性

原生产物按 RID **和构建配置**分别隔离。重建 Release 不会更新 Rider 中 Debug 会话使用的库。CPU 与 GPU 共用 ABI 2 压制请求；旧 ABI 1 库会在启动编码器之前失败。

启动 Debug 工作台前，应重建对应配置：

```powershell
pwsh -NoProfile -File ./build-debug-native.ps1
# Windows，包括 ARM64 宿主：
pwsh -NoProfile -File ./build-debug-native.ps1 -RuntimeIdentifier win-x64
```

`build-debug-native.ps1` 是固定使用 `Workbench / Debug` 的专用入口，复用现有环境检查，依次构建 Decoder → Audio → Export → Managed，使应用输出包含原生库。脚本支持 `build.ps1` 的 RID、FFmpeg／SDL SDK 路径、并发数、环境报告、依赖安装与定向测试选项；`-CheckEnvironment` 仅检查，不构建。普通 Debug 构建和 Rider 调试仍不会自动触发 native 编译。

压制 CMake 构建仅在原生库成功构建后写入 `aeginext_export.contract.sha256`。托管构建在复制原生产物前，将该指纹与当前公开压制头文件比较。`ANX1001` 表示原生库缺少当前接口元数据；`ANX1002` 表示库构建后接口已经变化。两类错误均包含 RID、配置、库路径和修复命令。未构建原生压制库的纯托管检出仍可编译，实际压制需要完整 Workbench 构建。

worker 还会检查实际加载的 ABI 和托管请求结构大小。不匹配时报告所需及实际 ABI、结构大小、进程架构和准确的加载路径。请勿绕过检查，或混用不同平台和 Debug／Release 的原生文件。

### 构建命令

在仓库根目录运行：

```powershell
# 只检查，不安装、不 restore、不构建。
pwsh -NoProfile -File ./build.ps1 -CheckEnvironment

# 额外检查媒体探测／编码工具；仍不安装、不构建。
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -CheckEnvironment

# 默认构建托管解决方案。
pwsh -NoProfile -File ./build.ps1

# macOS / Windows x64 首版完整环境检查和一键构建（不包含可选 HDR 预览）。
pwsh -NoProfile -File ./build.ps1 -Target Workbench -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release

# Windows：ARM64 或 x64 宿主均生成 win-x64，由 x64 MinGW 构建原生库。
pwsh -NoProfile -File ./build.ps1 -Target Workbench -RuntimeIdentifier win-x64 -Configuration Release

# 安装已识别的缺失包，重新检查，再构建。
pwsh -NoProfile -File ./build.ps1 -InstallDependencies

# 同时安装缺失的媒体工具，复查后构建托管工程。
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -InstallDependencies

# 仅运行所需测试；不传 -RunTests 就不运行测试。
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Media
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Desktop
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects 'Desktop.Ui'
pwsh -NoProfile -File ./build.ps1 -RunTests -TestProjects Application

# macOS：同时构建可选的 native 诊断库和托管工程。
pwsh -NoProfile -File ./build.ps1 -Target All -Configuration Release -RunTests -TestProjects Media

# macOS：只编译原生库并运行 CTest。
pwsh -NoProfile -File ./build.ps1 -Target Native -Configuration Debug -RunTests

# macOS / Windows x64：检查独立解码库所需环境，不构建、不安装。
pwsh -NoProfile -File ./build.ps1 -Target Decoder -CheckEnvironment

# 编译独立解码库并运行其 CTest；之后再构建 Managed 以复制库。
pwsh -NoProfile -File ./build.ps1 -Target Decoder -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Audio -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Export -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Managed

# 可显式选择 FFmpeg shared SDK；不接受只包含 ffmpeg.exe 的目录。
pwsh -NoProfile -File ./build.ps1 -Target Decoder -FfmpegRoot 'C:\SDKs\ffmpeg-9.0.2-full_build-shared' -CheckEnvironment

# 可选环境报告；相对路径以调用者当前目录解析。
pwsh -NoProfile -File ./build.ps1 -CheckEnvironment -ReportPath artifacts/verification/build-environment.json
```

`-CheckEnvironment` 与 `-InstallDependencies` 不能同时使用。只有明确传入 `-ReportPath` 才写环境报告；普通检查不创建构建目录。环境不完整退出码为 2；参数组合错误为 1；构建、测试或包管理器失败保留外部命令退出码，并停止后续步骤。

`-RunTests` 在 Managed 目标默认运行 Core、Application、Rendering、Media、Desktop 五组；可以用 `-TestProjects` 筛选，在 PowerShell 会话中用 `-TestProjects Media,Desktop` 选择多项。Desktop 的控制器测试不启动窗口。Native、Decoder、Audio、Export 目标只运行各自的 CTest，All 同时包含 HDR CTest 和选定的托管测试，不启动 GUI 诊断。All 保持原有语义，不包含 Decoder、Audio 或 Export。Linux 当前缺少 Rendering 测试所需原生资产，因此明确选择 Rendering 时失败；不能当作跳过后全部成功。

`Workbench` 一次检查全部首版构建依赖，顺序执行 Decoder → Audio → Export → Managed，使桌面输出包含三份原生库和导出 worker。`-RunTests` 时先运行三个原生 CTest，再运行选定的托管测试。合并检查包括固定 FFmpeg SDK／swresample、SDL3、.NET SDK 与原生 RID 一致性，并从所选 SDK 的 ffmpeg 核对 libx264/yuv420p、libx265/yuv420p10le、AAC/fltp、MP4/Matroska 封装能力。能力缺失报告 Invalid，不自动替换已有包。依赖默认只检查；只有显式 `-InstallDependencies` 才安装 Missing 项。Workbench 支持 macOS 与 Windows x64 构建入口，Linux 明确延期；Windows native、音频播放、实际 worker 压制和取消已在 Parallels Windows 11 验证，证据及未覆盖的系统要求见[本轮 Checkpoint](README.md#实施与验收记录)。

开发版本由 `Directory.Build.props` 固定为 `0.1.0`，程序集及文件版本为 `0.1.0.0`。`-RuntimeIdentifier` 支持 `osx-arm64`、`osx-x64`、`win-x64`，必须匹配构建平台；macOS 使用当前宿主架构，Windows 始终使用 x64 目标。Windows ARM64 的 .NET／PowerShell 宿主可以生成该目标，原生代码仍要求 x64 MinGW 编译器。Windows ARM64 运行的是 x64 应用，由 Windows 的模拟层执行。

## 环境检查与安装

托管目标检查 `dotnet` 是否存在，并在仓库目录实际执行 `dotnet --version`。当前 `global.json` 固定 10.0.401、`latestPatch`、禁止预览版本：同 feature band 的稳定补丁允许使用，10.0.400、10.0.500 或预览版本不能替代。SDK 命令存在但解析失败时，提示安装与仓库要求匹配的 SDK。

SDK 缺失时，macOS 安装 Homebrew `dotnet`，Windows 安装 Scoop `main/dotnet-sdk`。包名已核对 [Homebrew 官方 formula](https://formulae.brew.sh/formula/dotnet) 与 [Scoop 官方 manifest](https://github.com/ScoopInstaller/Main/blob/master/bucket/dotnet-sdk.json)。已有且可用的 SDK 不要求来自包管理器；仅构建托管目标时，包管理器缺失属于提示，安装依赖时才是阻塞。

`-WithMediaTools` 分别执行 PATH 选中的 `ffprobe -version` 和 `ffmpeg -version`，按 `src/AegiNext.Media/Probing/ffmpeg-toolchain.json` 验证程序发行字符串与 `libavutil`、`libavcodec`、`libavformat`、`libswscale`、`libswresample` 的编译／运行版本。当前锁定 FFmpeg 9.0.2，SDR 预览使用 libswscale 10.1.102，音频使用 libswresample 7.1.102；允许的发行字符串为 `9.0.2` 与 Scoop 使用的 `9.0.2-full_build-www.gyan.dev`，不接受其它后缀或开发版本。匹配规则来自同一份 manifest，版本错误、库版本缺失或工具执行失败均报告 Invalid。

媒体工具缺失时，显式安装在 macOS 使用 Homebrew `ffmpeg`，Windows 使用 Scoop `main/ffmpeg`；两个可执行文件缺失只安装同一包一次。Linux 需要自行准备匹配的工具，脚本不选择系统包管理器。该开关也可用于 Native 目标，但不会改变其平台限制；本步不验证滤镜、硬件编码器或 libplacebo 编码能力。

macOS Native 额外检查：

- 系统至少为 macOS 14、PowerShell 与宿主架构一致；All 还通过 MSBuild 属性检查实际 SDK RID 与原生目标一致，避免混合 Rosetta 依赖或构建成功但漏复制动态库。
- CMake 最低版本读取 `native/CMakeLists.txt`，Ninja、pkg-config 和需要测试时的 CTest。
- `xcrun` 能找到 Clang 与实际 macOS SDK 目录。Apple SDK 缺失或许可未处理时明确提示，不尝试用 Homebrew 替代。
- Homebrew 中的 libplacebo、MoltenVK、Vulkan-Headers，以及开发头文件、动态库和 pkg-config 文件。
- 依赖版本从 `native/dependencies.json` 读取；检查活动头文件版本和 libplacebo 的 Vulkan / vk_proc_addr / shaderc 能力，不仅检查包名或 DLL 是否存在。

`-InstallDependencies` 只安装报告为 Missing 且已映射到包的项，然后重新探测。已安装但版本不符的 Invalid 项不会被自动升级／降级。包仓库未来更新后，安装到最新包不一定满足本项目锁定版本，复查仍会失败；需要明确准备匹配的版本，脚本不会修改锁文件来迁就环境。

暂未实现的 Windows HDR native 不会触发安装路径，独立 Decoder 的构建与其分开。

## 独立解码库环境

`Decoder` 在 `native/decoder` 配置独立 CMake 项目，不要求 .NET、MoltenVK、libplacebo 或 pkg-config。构建助手位于 `scripts/build/AegiNext.Decoder.ps1`。FFmpeg SDK 根目录依次采用显式 `-FfmpegRoot`、`FFMPEG_DIR`，最后才查询 `brew --prefix ffmpeg` 或 `scoop prefix ffmpeg-shared`。显式目录不存在／不完整时报告 Invalid，不回退到另一份 SDK；`-FfmpegRoot` 用于 Decoder、Audio、Export、Workbench。

检查包括 CMake 最低版本（读取该子项目 CMakeLists）、Ninja、可选 CTest，以及：

- macOS：原生 arm64/x64 PowerShell、macOS 14+、`xcrun` 找到的 Clang／Clang++ 与 macOS SDK。
- Windows：x64 或 ARM64 PowerShell 宿主、`gcc` 与 `g++` 的 `-dumpmachine` 均为 `x86_64-w64-mingw32`。使用 Scoop `main/mingw`，不要求安装 Visual Studio；不以宿主 CPU 架构代替编译器目标检查。
- FFmpeg：avutil、avcodec、avformat、swscale、swresample 五个库的完整开发头文件及精确版本，macOS dylib 或 Windows 导入库和对应主版本 DLL，所选包内 `ffmpeg`／`ffprobe` 报告的编译与运行库版本。版本统一读取 `ffmpeg-toolchain.json`。

Windows 开发 SDK 使用 [Scoop `main/ffmpeg-shared`](https://github.com/ScoopInstaller/Main/blob/master/bucket/ffmpeg-shared.json)，其 Gyan shared 发行包含开发文件并设置 `FFMPEG_DIR`。普通 `main/ffmpeg` 只满足 CLI 检查，不能替代 SDK。[Gyan 发行说明](https://www.gyan.dev/ffmpeg/builds/)

显式传入 `-InstallDependencies` 时，macOS 缺项使用 Homebrew `cmake`、`ninja`、`ffmpeg`；Windows 缺项使用 Scoop `main/cmake`、`main/ninja`、`main/mingw`、`main/ffmpeg-shared`。Apple SDK 仍需用户安装／选择。已有版本不匹配的工具不自动替换。安装后重新检查；Scoop 新包尚未进入当前 PATH 时也可通过包前缀定位工具。

## 音频和压制原生库

`Audio` 与 `Export` 分别构建 `native/audio` 和 `native/export`，支持 macOS 与 Windows x64 的独立检查／构建入口；Linux 当前明确延期。两者复用 Decoder 的编译器与 FFmpeg SDK 检查。Audio 额外要求 SDL3 3.4.16：优先显式 `-SdlRoot`，其次 `SDL3_DIR`，最后查询 Homebrew `sdl3` 或 Scoop `aeginext-sdl3`。检查开发头、CMake 包、导入库和运行库；加载输出设备时再次核对 SDL 编译及运行版本。

只在 Audio 或 Workbench 目标显式指定 `-InstallDependencies` 且 SDL 缺失时安装。macOS 使用 Homebrew `sdl3`；Windows 使用仓库 `scripts/build/scoop/aeginext-sdl3.json`，固定 [SDL 官方 3.4.16 MinGW 开发包](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.16) 与 SHA-256，选取 x86_64 SDK。此时 `-SdlRoot` 可指定自行准备的同版本 SDK。普通 Managed、Decoder 和 Export 不要求 SDL。Windows 构建把所选 SDK 的 `bin/SDL3.dll` 和所选 FFmpeg SDK 的 DLL 放在相同 RID／配置目录；Windows 实机运行仍需验收。

Workbench 同样接受 `-SdlRoot`，并沿用上述显式安装策略。开发运行时应让所选 SDK 的 ffmpeg／ffprobe 位于 PATH，或分别设置 `AEGINEXT_FFMPEG_PATH`、`AEGINEXT_FFPROBE_PATH` 为其绝对路径；构建不会改写用户的持久环境设置。

预览音频转换为 48 kHz 立体声浮点 PCM，设备队列最多 250 ms，通常预填充 200 ms。影音时钟使用已提交样本减去 SDL 尚未消费样本，并减去一块设备缓冲作为延迟估计；这不是硬件 DAC 精确时钟。音轨起点／间隙补静音，短音轨结束后继续静音推进至视频结尾。无音轨或设备失败时视频使用单调软件时钟，错误通过预览快照公开。语谱分析使用独立的 16 kHz 单声道解码器，不占用播放队列。

托管音频集成测试默认明确跳过；运行前设置 `AEGINEXT_RUN_AUDIO_TESTS=1` 和完整路径 `AEGINEXT_FFMPEG_PATH`，并先构建相同配置的 Audio 库。纯托管音频／控制器／谱图测试不要求音频设备；原生 CTest 使用 SDL dummy 驱动，不代表实际扬声器验收。

媒体进程测试使用单独的 `AegiNext.Media.TestHost` 自包含 apphost，构建测试项目时将完整运行时复制至测试输出的 `ProbeTestHost` 子目录。它跟随显式测试 RID，未指定时采用 SDK RID；运行 fixture 不借用 `DOTNET_HOST_PATH`，避免 Windows ARM64 SDK 启动 x64 fixture 时发生架构冲突。参数、管道容量、超时与取消测试仍调用真实子进程。

## 构建产物与配置隔离

原生 CMake/Ninja 缓存位于 `artifacts/native/build-macos-<arch>-<configuration>`，可复制库位于 `artifacts/native/<rid>/<Configuration>`，例如：

```text
artifacts/native/osx-arm64/Debug/libaeginext_media.dylib
artifacts/native/osx-arm64/Release/libaeginext_media.dylib
artifacts/native/osx-arm64/Release/libaeginext_decode.dylib
artifacts/native/osx-arm64/Release/libaeginext_audio.dylib
artifacts/native/osx-arm64/Release/libaeginext_export.dylib
artifacts/native/win-x64/Release/aeginext_decode.dll
artifacts/native/win-x64/Release/aeginext_audio.dll
artifacts/native/win-x64/Release/aeginext_export.dll
```

Decoder、Audio、Export 缓存分别放在 `artifacts/native/build-<component>-<rid>-<configuration>`，以 `AEGINEXT_FFMPEG_ROOT` 固定开发包、`AEGINEXT_NATIVE_OUTPUT_DIR` 固定产物路径；Audio 另传 `AEGINEXT_SDL_ROOT`。Windows 构建前只复制所选开发包 `bin` 内的 DLL 到同一 RID／Configuration 目录，供测试和托管程序加载，不复制 CLI 可执行文件。该目录用于开发运行；完整媒体工具、递归依赖、许可证及文件清单由独立 `publish.ps1` 收集。

缓存和产物同时隔离，避免 Debug 编译出的新文件让 Release 增量构建误认为目标已更新。Media 工程优先按显式 `RuntimeIdentifier`、否则按 SDK RID，并结合 `Configuration` 选择复制文件。普通 Managed 构建没有原生文件时仍可完成；可选 HDR 入口会明确报加载失败。

Decoder、Audio、Export 配置前会核对现有 `CMakeCache.txt` 的源目录、构建目录、生成器与 C/C++ 编译器路径。编译器路径改变时，仅对确认归属本次组件／RID／配置的缓存使用 CMake `--fresh`，同时重新传入完整 SDK 参数，避免 CMake 自动重配时丢失显式 FFmpeg／SDL 路径。编译器不变时保留增量缓存；目录归属不明或不匹配时明确失败，不自动清理。独立 RID／配置产物目录保持不变。

`scripts/build-native-macos.sh` 保留为 PowerShell 入口的薄封装，支持透传 `-Configuration`、`-RunTests` 等参数；默认配置改为 Release。旧 `AEGINEXT_NATIVE_BUILD_DIR` 不再接受，以确保配置隔离；`AEGINEXT_NATIVE_BUILD_JOBS` 仍可指定并发数，默认 2。

托管 restore 使用 `--locked-mode`；build 使用 `--no-restore`；test 使用相同配置的 `--no-restore` 并构建选定的测试项目。默认 `Release|Any CPU` 方案配置没有启用全部测试项目的构建，因此不能用 `--no-build` 假设其 DLL 已存在。显式 RID 的 restore、build、test／publish 都传 `-p:AegiNextRuntimeIdentifier=<RID>`，让早期项目求值选择正确锁文件及中间目录。restore 的 `-r` 单独可能只设置复数 `RuntimeIdentifiers`；方案 build 则不能使用 `-r`。项目级 test／publish 保留 `-r` 并携带相同属性桥接。

无 RID 开发还原使用各项目的 `packages.lock.json`；显式 RID 分别使用 `packages.osx-arm64.lock.json`、`packages.osx-x64.lock.json`、`packages.win-x64.lock.json`。Windows 默认设置 `RuntimeIdentifier=win-x64`，即使未写 `-r` 也采用该锁。显式 RID 的中间文件位于 `obj/<rid>`，编译源排除所有 `obj`／`bin` 目录，避免共享仓库的旧生成代码混入另一平台。依赖变化时应分别维护各 RID 锁；正常构建不自动接受已有锁的依赖变化。维护命令见[平台发布说明](publishing.md)。临时工作目录、原生包搜索环境和调用者的 `LASTEXITCODE` 在命令结束或失败时恢复。

原生开发库仍链接本机 Homebrew。设置 macOS 14 部署目标不能消除依赖本身以 macOS 27 构建的限制；发布器记录所有实际二进制要求的最大最低系统版本，并写入 app 的 `LSMinimumSystemVersion`。

## 自包含发布

`publish.ps1` 在对应平台构建和打包：macOS 上生成当前架构 app，Windows 上统一生成 win-x64 目录包。发布器不安装依赖；缺少开发 SDK、运行库或原始许可证时明确失败，输出目录必须为新目录以保留旧产物。

```powershell
# 对应架构 Mac；默认先构建 Workbench 及可选 HDR 原生库。
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier osx-arm64 -OutputDirectory ./artifacts/releases/mac-local

# Windows 本机，ARM64 宿主也使用同一 x64 目标。
pwsh -NoProfile -File ./publish.ps1 -RuntimeIdentifier win-x64 -FfmpegRoot C:/SDK/ffmpeg-shared -SdlRoot C:/SDK/SDL3 -OutputDirectory ./artifacts/releases/windows-local

# 移动完整发布目录后，只读验证全部签名后文件的大小与哈希。
pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory ./artifacts/releases/mac-local
```

包内包含自包含 .NET 10、主程序、独立 ExportWorker、`tools/ffmpeg`／`tools/ffprobe`、自有媒体模块及非系统递归动态依赖。macOS 改写 Mach-O 相对加载路径并进行签名验证，Windows 通过 PE 导入表补齐所选 SDK、已验证 x64 编译器目录及显式运行库目录中的 DLL，第三方 VC／MinGW 运行库也需要包含。编译器运行库自动收集对应前缀的原始许可证。`-SkipBuild` 仅复用相同 RID／配置的 native 产物，仍重新 publish 托管主程序和 worker。

`media-runtime.json` 标记完整媒体发布；工具解析在此模式使用包内工具，缺失时报告完整运行时错误。开发运行保留显式路径和环境变量配置。自有原生模块从应用目录加载，加载失败包含进程架构、库路径以及底层错误，区分缺文件与依赖／架构不匹配。

外部 `package-manifest.json` 记录 `0.1.0` 产品版本、RID、Git SHA／工作区改动状态、构建时间、包内工具实际版本首行、两个自包含应用的运行时框架、原生依赖来源、系统要求、许可证来源及签名后文件哈希。Windows 系统要求分为官方 .NET 10 生命周期政策和完整 native 应用的实机验证，不以构建机器版本推定最低要求。`-RuntimeDependencyDirectory` 可补充 SDK 外的运行库；`-LicenseDirectory` 用对应包名子目录补充原始 notices。详细目录结构及迁移后验证步骤见[平台发布说明](publishing.md)。发布验证和真实媒体打开、播放、音频、worker 压制及取消验收分别记录。

## 无窗口 UI 验证

`Desktop.Ui` 是独立的 Avalonia Headless / xunit.v3 测试项目，与原有 Desktop 的 xunit2 测试隔离。它加载真实 XAML、控件、命令和键盘路由，通过可控媒体源验证打轴及关闭生命周期；不启动用户桌面的原生窗口。可以直接运行：

```powershell
dotnet test ./Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj -c Release
```

为布局检查指定绝对路径 `AEGINEXT_UI_CAPTURE_DIRECTORY` 后运行完整 UI 测试，会保存主窗口及设置三页的浅色英文／深色中文 PNG。普通测试不输出图片。Headless 不等同 macOS 原生菜单或真实视频设备验收；当前 Linux 缺少所需 Rendering 原生资产，脚本明确拒绝选择此组。

## 脚本验证

```powershell
# 首次显式下载固定版本 QA 模块到项目 artifacts，不安装全局模块。
pwsh -NoProfile -File ./scripts/test-build.ps1 -RestoreTools

# 后续离线使用已缓存工具。
pwsh -NoProfile -File ./scripts/test-build.ps1
```

该入口固定 Pester 5.7.1、PSScriptAnalyzer 1.24.0，先静态检查再执行 `Tests/Build`。普通构建不依赖这两项 QA 工具。测试不实际安装包；Windows/Linux 分支通过隔离的命令模拟验证，不能代替这些系统上的真实运行。

生产脚本按完整 warning/error 规则检查。仅测试文件排除跨 Pester 生命周期块的未使用变量误报，以及 TestDrive fixture 函数的 ShouldProcess 规则；其余规则保留。测试报告写入 `artifacts/verification/build-scripts-tests.xml`。
