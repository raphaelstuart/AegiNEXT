# 架构

[English](../en/architecture.md) · [简体中文](architecture.md) · [全部指南](README.md)

## 查找职责归属

| 位置 | 职责 |
|---|---|
| `src/AegiNext.Core` | 有理时间、不可变工程/场景模型、验证、动画、蒙版和特效 DSL |
| `src/AegiNext.Application` | 编辑事务、Undo/Redo、存储/资源和字幕格式交换 |
| `src/AegiNext.Rendering` | 独立于 UI 的 Skia/HarfBuzz 排版、场景几何和线性 F16 合成 |
| `src/AegiNext.Media` | 探测、帧所有权、原生适配、播放/分析、预览和导出编排 |
| `src/AegiNext.Desktop` | Avalonia 工作区、面板、控件、菜单、偏好和平台宿主 |
| `src/AegiNext.ExportWorker` | 共享 Core 与 Rendering 的独立 `aegn-exporter` 进程 |
| `native/decoder`、`native/audio`、`native/export` | 独立 FFmpeg/SDL3 C ABI |
| `native/` | 可选 macOS HDR 诊断后端 |
| `Tests/`、`scripts/` | 定向验证、构建和打包工具 |

Core 不依赖 Avalonia、Dock、FFmpeg、Skia 或文件系统。Application 负责业务修改，Desktop 组合服务；Rendering 和 Media 使用显式契约与资源所有权。业务 ViewModel 不持有控件、Dock 对象或 Bitmap。

## 时间与存储

时间采用有理数 `MediaTime`，Clip 可见范围为 `[Start, End)`。解码与 VFR 使用真实呈现时间戳；字幕格式交换只在边界映射已确认的播放原点，保留动画/卡拉 OK 相对时间。

新 `.aeginext` 文件使用 **v6**。v3–v5 迁移不移动字幕/图层时间，未知播放原点保持未知直到确认。兼容缺失/null 的旧蒙版，不支持的非空旧局部蒙版会标明所属对象并拒绝读取；不支持 v1/v2。

读取校验字段、引用、重叠、几何和预算；保存通过同目录临时文件原子替换。视频只引用、不复制，托管资源使用受限相对路径与可选哈希。另存为重定位资源/引用；跨目录迁移清除可能恢复旧路径的历史。

自动保存通过串行持久化协调器捕获已提交内容和视图状态，不提交草稿、不抢焦点、不改变 Undo。保存旧快照时，较新的修改仍保持未保存。

## 编辑与呈现

`ProjectEditor` 提交不可变快照，多 Clip 编辑/导入使用原子事务；校验失败不改变工程和历史。布局变化保留同一会话与固定面板实例。个人布局、样式、脚本、偏好和日志与工程内容独立。

预览复用编辑/导出的场景几何，显示 SDR 派生图；渲染器在显示/编码前保持线性 F16，worker 导出读取原始媒体帧。异步结果交付前检查时间、修订、请求身份和所有权。

## 参与开发

C# 使用 Allman 花括号、文件级命名空间、单文件单顶层类型，适当使用 `var` 和目标类型 `new()`。开启可空检查、分析器和警告视为错误，公开/受保护 API 文档描述契约。

测试放在所属 Tests 项目并定向运行。修改原生契约时同步托管绑定、worker 协议、能力/版本检查与布局测试。提交围绕明确范围，采用简短英文 `feat:`、`fix:` 或 `chore:` 标题；排除生成产物，执行 `git diff --check` 和受影响测试。

继续阅读[工作区集成](composable-workspace.md)、[媒体](media.md)、[渲染](rendering.md)或[构建](building.md)。
