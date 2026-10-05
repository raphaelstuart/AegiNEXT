# AegiNext 文档

[English](../README.md) | [简体中文](README.md)

面向使用者的操作手册和面向开发者的技术文档统一放在 `docs/`。当前阶段 Checkpoint 与验收记录位于 `docs/checkpoints/`。

## 使用手册

- [Quick Start：用户快速开始](quick-start.md)：从启动到完成第一条字幕、保存工程和压制成片。
- [工作台操作说明](workbench.md)：轨道、时间线、样式、关键帧、布局、设置和日志。
- [压制与 HDR 边界](export.md)：编码选项、音频处理、输出限制和色彩契约。
- [特效 DSL 与样板](effect-dsl.md)：固定／自由时间段、向量、完整 RGBA、关键帧与设置编辑器。

## 开发文档

| 主题 | 文档 |
|---|---|
| 模块职责与依赖方向 | [架构](architecture.md) |
| SDK、native、RID、依赖锁与定向测试 | [跨平台构建](building.md) |
| 自包含发布、依赖闭包、签名与搬移验证 | [平台发布](publishing.md) |
| 会话、MVVM、功能面板和通用子控件 | [可组合工作区](composable-workspace.md) |
| Dock 空间、布局预设、浮窗与持久化 | [布局边界](layouts.md) |
| 系统按钮、自建标题栏与跨窗口菜单 | [窗口基础](workspace-windowing.md) |
| 媒体事实、FFprobe 与进程边界 | [媒体探测](media-probing.md) |
| 视频解码、定位、PTS 与帧生命周期 | [视频解码](video-decoding.md) |
| 播放时钟、音频与调度 | [播放基础](video-playback.md) |
| SDR 预览、色彩派生与显示链路 | [视频预览](video-preview.md) |
| F16 合成、文字塑形、路径与渲染接口 | [渲染契约](rendering.md) |
| 可选的 macOS 原生 HDR 诊断 | [原生 HDR](native-hdr.md) |
| 拖动预览性能与测量边界 | [交互预览性能](interactive-preview-performance.md) |

项目专用 Skill：调用 `$aeginext-effect-dsl` 编写字幕脚本，或 `$aeginext-controls` 开发、接入共享控件；定义见 [DSL Skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md) 和 [控件 Skill](../../.agents/skills/aeginext-controls/SKILL.md)。

## 实施与验收记录

- [预览解码切换、缺失颜色标签与平台验收](checkpoints/video-decode-modes-and-missing-color.md)

- [预览画质、轨道样式策略、双语文档与 Skill](checkpoints/preview-quality-and-track-style-policy.md)

- [轨道样式、自动位置、向量输入与逐字高亮](checkpoints/track-styles-and-karaoke.md)
- [GPU 编码选项、工程标题与恢复默认配色](checkpoints/export-title-colors.md)
- [无框标签、时间线显示与主题、DSL 自动补全](checkpoints/timeline-completion-polish.md)

技术文档中引用的测试结果和平台能力有各自的验证范围。查看当前完成度时，以对应阶段记录为准；计划中的功能不代表当前界面已经提供入口。
