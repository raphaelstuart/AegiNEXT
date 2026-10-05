# GPU 编码选项、工程标题与恢复默认配色

[English](../../checkpoints/export-title-colors.md) | [简体中文](export-title-colors.md)

日期：2026-10-05。基于已有未提交内容继续开发；保留用户暂存和无关修改，不暂存或提交。产品版本保持 `0.1.0`，工程格式保持 v3。

## Phase 1：配色恢复

Checkpoint：恢复默认配色立即应用、持久化，保持其他设置与工程。

- 配色页增加“恢复默认配色”。从 `WorkbenchPreferences` 默认值恢复高亮色和完整音频图方案；一次发出颜色修改事件，由现有 Workspace 偏好协调保存。
- 清除这几个颜色字段的非法或未完成草稿；回填不会触发第二次颜色写回。主题、语言、菜单模式、快捷键、音量保持原值。
- 控件只负责按钮与绑定，ViewModel 持有颜色输入草稿，工程与布局不参与此操作。

## Phase 2：标题投影

Checkpoint：名称同源，另存为成功后同步刷新，取消和失败保留原名。

- `WorkbenchProjectTitle` 按工程文件 basename、显式工程名称、绑定视频 basename、本地化未命名依次选择显示名。
- 主窗标题为 `AegiNEXT - 工程名`，未保存时追加 ` •`；浮窗追加活动面板名。原生 `Window.Title` 与自建标题栏通过现有注册表同步。
- 保存和压制的建议文件名使用同一 `session.ProjectDisplayName`。名称只作显示投影，不改 `ProjectDocument.Name`、工程格式或 Undo。
- 验证打开／新建、中文文件名、取消、保存失败、另存为成功、Undo／Redo、语言及浮窗同步。

## Phase 3：真正的 GPU 视频编码

Checkpoint：UI 选择穿过不可变请求和独立 worker，初始化实际硬件后端，不静默退回 CPU。

- 默认 CPU 软件编码，保留 `libx264`／`libx265` 的 CRF 与速度。GPU 使用 0.1–200 Mbps 目标码率，默认 8 Mbps；两个模式保留各自草稿，只验证当前模式字段。
- macOS 使用 VideoToolbox，`allow_sw=0`、`require_sw=0`。Windows 依次尝试 NVENC、QSV、AMF，核对像素格式并实际初始化，失败时汇总具体诊断。
- 速度映射到各后端实际选项：VideoToolbox `prio_speed`、NVENC p3/p4/p5、QSV fast/medium/slow、AMF speed/balanced/quality。不同后端不承诺相同质量或速度。
- GPU 支持 SDR H.264 NV12 和 HEVC P010 10-bit。HDR 的已验证元数据契约仍使用软件 HEVC；GPU HDR 明确拒绝。
- Native Export ABI 升级为 2，72-byte 请求显式包含编码模式和码率；托管入口、C++、worker 与测试同步升级。
- 进度、结果和日志传递实际编码器名。硬件请求完成时再次验证 worker 返回的硬件身份，混用旧 worker 时拒绝原子提交输出。
- 解码与线性字幕合成保持现有路径；此选项只控制视频编码加速。

## Phase 4：定向验证与开发包

Checkpoint：自动测试、原生运行与视觉／物理硬件验收分别记录。

全部构建与测试串行执行，UI 测试关闭创建的主窗、浮窗和设置窗。日志前缀为 `artifacts/verification/export-title-colors-`。

| 检查 | 结果 | 证据 |
|---|---|---|
| 配色、名称来源、保存／新建／打开、Undo 和窗口注册领域回归 | 17 / 17 通过，无跳过 | `domain.log` |
| 设置实际颜色输入与恢复、中文媒体名、主窗／浮窗标题、GPU 选项与布局／导出生命周期 | 10 / 10 通过，无跳过 | `ui-final.log` |
| macOS 包内 worker、编码参数／ABI／wire／身份、CPU HDR／音频／取消、实际 GPU H.264／HEVC | 33 / 33 通过，无跳过 | `media-macos-final.log` |
| Windows x64 包内 worker、GPU 不可用与 CPU 恢复、HDR／音频／取消及身份回归 | 33 / 33 通过，无跳过 | `media-windows.log` |
| macOS Export native 构建与色彩／ABI CTest | 2 / 2 通过 | `native-macos-build.log`、`native-macos-tests.log` |
| Windows x64 Export native 构建与色彩／ABI CTest | 2 / 2 通过 | `native-windows-build.log`、`native-windows-tests.log` |
| macOS native 实际 GPU | 10 帧，`h264_videotoolbox` | `native-gpu-macos.log` |
| Windows native 实际虚拟机 | NVENC／QSV／AMF 初始化失败明确返回；随后 `libx264` 完成 10 帧 | `native-windows-probe.json`、`native-gpu-windows.log`、`native-cpu-windows.log` |
| 两平台自包含发布 | 0.1.0、主程序／worker／工具／native 完整，发布完成 | `publish-macos.log`、`publish-windows.log` |
| 清除开发 PATH 和媒体环境后的独立包探针 | 两平台均完成真实音频设备播放、271 帧字幕成片、颜色动画回读及取消清理 | `package-macos.json`、`package-windows.json` |
| 两平台包内文件哈希 | macOS 324 个、Windows 313 个文件全部匹配，无额外文件 | `package-hashes.json` |
| 两平台 ZIP 完整性 | `unzip -tq` 均通过 | 本轮终端执行 |
| macOS 签名 | `codesign --verify --deep --strict` 通过 | 本轮终端执行 |

GPU 成片测试除后端、H.264 8-bit／HEVC 10-bit、BT.709 与 AAC 外，还回读首帧 PTS 和叠层区域实际亮度；CPU HDR 回归保留原有 PQ／HLG 像素与元数据断言。最后 33 项使用最终发布包内 worker 和 FFmpeg／FFprobe，测试环境显式提供工具，未以 skip 计入通过。

Rider 受影响文件错误级分析未发现新增错误。`VideoExporter.ExportAsync` 的既有“实例成员可改为 static”诊断仍出现；基线已有 CA1822 抑制，保留应用服务实例边界，托管编译零警告／错误。`git diff --check` 通过。

Windows 包从本机 C: 发布目录搬到共享 X: 工程目录后执行上述回归与独立探针，进程架构为 X64，不依赖系统 dotnet 或开发工具 PATH。首轮发布使用相对输出路径而落在 Windows 进程的 C: 工作目录，已搬移到下列最终目录，验证脚本改为绝对输出路径。

## 开发包与验收边界

- macOS：`artifacts/releases/export-title-colors-osx-arm64/AegiNext.app`，ZIP 为 `AegiNext-0.1.0-osx-arm64-export-title-colors.zip`。
- Windows：`artifacts/releases/export-title-colors-win-x64/AegiNext/aegi-next.exe`，ZIP 为 `AegiNext-0.1.0-win-x64-export-title-colors.zip`。
- 包均包含自包含 .NET、独立 worker、FFmpeg／FFprobe、音频与导出 native、依赖闭包及许可／文件 manifest。保留旧开发包。
- 当前 macOS FFmpeg 依赖要求 macOS 27.0，最终 manifest 和 plist 均按实际闭包记录 27.0；此包不宣称支持 macOS 14。
- Parallels 未暴露 NVIDIA／Intel／AMD 的硬件编码能力，Windows 已验收明确失败与 CPU 恢复。物理 Windows GPU 的成功编码、驱动差异以及跨 DPI 视觉仍待对应设备人工验收。
- 本轮 UI 回归验证实际控件输入和窗口状态，复杂桌面观感仍由用户在开发包中验收；安装器、公证和 Windows 代码签名不在本轮范围。

操作说明见 [Quick Start](../quick-start.md)、[工作台手册](../workbench.md) 与 [压制说明](../export.md)。
