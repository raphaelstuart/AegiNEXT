# 轨道样式、自动位置、向量输入与逐字高亮

[English](../../checkpoints/track-styles-and-karaoke.md) | [简体中文](track-styles-and-karaoke.md)

日期：2026-10-05。产品版本 `0.1.0`，项目格式 v3。基于既有工作区继续修改，构建及测试串行执行。

这是此前轨道样式阶段的历史验收记录；后续优化已移除全部轨道应用，并新增自动应用开关与现有 Clip 确认。当前操作以 [工作台手册](../workbench.md) 为准。

## Phase 1：提交此前生产修改

Checkpoint：提交范围明确排除测试及文档，不夹带本轮新功能。

- 提交 `2fc544303371a4d29b63117b7e77e5eb0e17601f`：`feat: add hardware export and refine project title and colors`。
- 共 29 个生产文件；核对路径后确认没有 `Tests/`、`docs/` 或 native 测试文件。
- 测试和文档的已有工作区修改保留。本轮后续功能保留未提交状态，暂存区为空。

## Phase 2：轨道字幕样式

Checkpoint：当前轨道及全部轨道可一次套用；后续创建继承，Undo 和保存正确。

- 此阶段时间线右键曾增加“轨道字幕样式”与“全部轨道字幕样式”；后者已在后续优化中移除，不是当前入口。其实现按稳定 Track ID／Preset ID 调用；空轨道也可设置。
- `SubtitleTrack.DefaultStyle` 保存项目内的默认样式快照，记录来源 ID／名称。个人库修改、删除不改变该快照。
- 套用当前轨道或全部轨道时，现有字幕、轨道默认值及字体资源在一个事务中提交。保留时间、效果层、关键帧和逐字片段。
- 手动新增、实际新增快捷键、F8 打轴和指定轨道导入继承默认样式。拆分及跨轨移动保留已有 Clip 自己的样式。
- 字体准备通过 Application 的 `SubtitleStylePresetService.PrepareAsync` 完成，可处理零字幕项目。空间管理留在 Layouts，菜单局部交互留在 Timeline 面板，项目协调留在 Workspace。

## Phase 3：自动位置与共享向量输入

Checkpoint：居中自动位置 X 精确为零，渲染和测量一致，字段恢复不丢其他草稿。

- 自动水平排版改按真实字形边界对齐，消除排版宽度与 ink bounds 不一致导致的 X 偏移；居中为零，左／右对齐使用正／负 Margin。
- 自动转显式位置逐像素一致，恢复自动位置清除既有位置编辑并支持一次 Undo。
- Anchor、Pivot、Offset 使用三行共享 `VectorDraftInput`，锚点和轴心步长 0.1，像素偏移步长 1。
- 稳定分量字段由控件直接定位，不依赖外部 XAML 名称范围。Esc 只恢复当前分量，保留其他未完成草稿。

## Phase 4：逐字高亮样式

Checkpoint：选择预设保持原字形排版，已有时间保留，项目和 worker 同源。

- 样式面板新增“高亮样式”选择，含默认高亮；已有逐字高亮按钮执行应用。选择、语言往返与普通刷新不写项目。
- 按本轮保守范围，仅应用填充、描边、描边宽度和阴影，保留原字体、字号、对齐和位置。
- 每条字幕保存单份 `KaraokeHighlightStyle` 视觉快照，支持线性 HDR；已有逐字片段换样式时保留时间和字素范围。
- 渲染沿用同一塑形结果，先从高亮区域扣除基础外观，再绘制高亮外观，避免较大基础描边或阴影残留。
- 拆分保留有高亮侧的快照；合并双方有高亮时检查实际视觉兼容，来源 ID／名称不影响比较。不同外观明确拒绝，只有一方有高亮时使用该方快照。
- 清除同步移除逐字片段与高亮快照。缺失新增字段的 v3 保持旧高亮行为，未知字段及重复键继续拒绝。

## Phase 5：跨平台验收与文档

Checkpoint：两平台受影响回归、独立 worker 成片、静态分析和 Git 范围检查通过。

| 定向范围 | macOS | Windows x64 / Parallels ARM64 |
|---|---:|---:|
| Core：位置与高亮模型、HDR、非法数据 | 16 通过 | 16 通过 |
| Application：轨道／预设、位置、字幕事务及高亮保存／Undo | 56 通过 | 56 通过 |
| Rendering：实际字形位置、多行高亮、填充／描边／阴影像素 | 13 通过 | 13 通过 |
| Desktop：位置草稿和测量 | 7 通过 | 7 通过 |
| Headless UI：轨道菜单与键盘、向量输入、高亮、设置和全局输入 | 38 通过 | 38 通过 |
| Media：真实 wire 及独立 worker 高亮成片 | 3 通过 | 3 通过 |

共 133 项定向用例在每个平台完成，无跳过。UI 测试关闭其创建的主窗、设置窗、浮窗及独立宿主。

真实成片用例将轨道默认样式和非空高亮快照传给本轮构建的独立 worker：生成两份 H.264 成片，回读首帧确认起点保持基础样式，回读下一帧确认使用保存的红色高亮，而不是片段中的白色兼容值，并校验 PTS 与临时文件清理。验收使用本轮源码 Release 构建的主程序及 worker；没有重打完整开发发布 ZIP，不代表此前包已包含本轮修改。

验证过程中修正三个测试问题：独立窗口没有父 XAML 名称范围、ImmutableArray 往返需要比较内容、wire 的项目 JSON 由 worker 中的 ProjectStore 解码而不是直接使用消息 options 解码 MediaTime。macOS Application 首次 55 通过、1 项内容断言修复后重跑通过；macOS Media 首次 2 通过、1 项 wire 边界断言修复后重跑通过。

Windows 扩展回归发现原有输入路由问题：顶部菜单项仅获得焦点但没有展开时仍被当成菜单输入上下文，阻断 F8。输入策略改为检查实际展开状态，保留 PopupRoot、展开菜单、下拉框、文本及模态例外；增加关闭／展开菜单回归，Windows 最终 38 项 UI 全部通过。原失败用例保留明确的焦点、展开状态、位置与错误诊断。

Rider 受影响生产代码和测试的 error 分析无新增错误，构建无警告／错误；`git diff --check` 通过。结果位于 `artifacts/verification/track-styles-*.trx`，Windows 分组日志为 `track-styles-windows-*.log`；macOS 最终 UI 为 `track-styles-ui-final.trx`，Windows 为 `track-styles-windows-ui-final.trx`。

人工视觉验收仍覆盖窄停靠区域、跨 DPI、真实拖动与混合字体观感；上述自动回归验证真实控件输入、布局几何、渲染像素及子进程成片。

使用说明见 [Quick Start](../quick-start.md)、[工作台手册](../workbench.md)，分层说明见 [架构文档](../architecture.md)。
