# 无框标签、时间线显示与主题、DSL 自动补全

[English](../../checkpoints/timeline-completion-polish.md) | [简体中文](timeline-completion-polish.md)

日期：2026-10-05。基于当前工作区继续优化；不暂存、不提交、不回退用户修改。产品版本保持 `0.1.0`。

## Phase 1：Dock 框线

Checkpoint：布局层统一外框，多标签只高亮文字和底色。

- Dock 默认 ToolTabStrip 的左右填充区即使只有一个标签也画顶线；选中 ToolTabStripItem 另画左右及底边，导致圆角裁切和残留线。
- 移除填充区边线及标签普通／选中／悬停状态描边。选中只显示主色文字与 14% 主色底色；选中前后 Padding 相同。
- Preview、Subtitles、Timeline 根 Border 不再重复绘制外框。空间层保留统一圆角、背景、描边和裁剪；保留 Dock 的原模板、命令、拖拽及生命周期。

## Phase 2：时间线显示与吸附

Checkpoint：能量与波形独立隐藏，吸附指示对应真实结果。

- 新增两个独立工具按钮，通过 Timeline VM 状态和控件 StyledProperty 切换能量图／波形绘制。分析数据不清除、不重新分析，视窗与播放位置不变；固定面板浮动或重新停靠时保留当前开关。开关为会话状态，不新增跨重启偏好。
- 吸附按钮改用磁铁。Quantization 返回值包含精确吸附 Boundary，拖动时使用同一结果画竖向框线；Alt、无效放置、夹紧后错开、释放和取消清除指示。
- 深浅时间线绘制调色板覆盖背景、轨道、Clip、文字、曲线、标记、播放头与吸附位置。展开动画行增加轻薄底板，保证曲线在能量图上可读。

## Phase 3：方案选择与浅色音频配色

Checkpoint：内置方案可直接往返，浅色适配不改写个人色值。

- 配色 ComboBox 改为稳定方案对象的 SelectedItem；语言刷新只更新显示文字，不重建 ItemsSource。当前方案回填不再通过失效索引污染选择。
- 经典、冰蓝、暖焰、灰阶按实际主题解析为对应深浅配色。经典深色保留原色表；浅色使用明亮低能量与深高能量色。设置示意图和实际时间线复用同一解析。
- `AdaptToTheme` 缺省为 true，旧偏好按原方案识别；手动编辑或选自定义时为 false，保留用户色值。颜色与旧字段继续独立持久化，不修改项目。自定义可以直接切回经典。

## Phase 4：DSL 补全

Checkpoint：提示在光标附近实际可见，保留原生编辑和诊断。

- 真实 TextInput 提交后自动显示上下文候选，Ctrl/Cmd＋Space 可显式打开。Popup 使用当前窗口 Overlay，锚定原生 TextLayout 的光标位置，不占编辑器底部布局、不抢焦点。
- 保留上下键选择、Enter／Tab 插入、Esc 和指针插入。输入法预编辑时收起浮层；失焦、只读及离开宿主取消待处理请求并关闭。
- 移除底部常驻操作提示，保留解析诊断和定位入口。补全支持 Tab 等空白分隔符。浮层使用实际 PreviewSurface／PreviewBorder 主题资源，深浅背景和边框均验证实际像素。

## 验证

| 检查 | 结果 | 证据 |
|---|---|---|
| 配色／偏好／草稿、DSL 补全上下文、吸附结果等领域回归 | 103 / 103 通过 | `artifacts/verification/timeline-polish-domain.log` |
| 工作台、设置、窄浮窗、时间线、DSL 真实输入及像素回归 | 85 / 85 通过 | `artifacts/verification/timeline-polish-ui.log` |
| Rider 受影响文件错误级分析 | 无 error | 根 Agent 与三个分工 Agent 的 lint_files |
| macOS Release 构建 | 通过 | 上述构建测试日志 |
| Windows x64 自包含验证构建 | 通过 | `artifacts/verification/timeline-polish-windows-build.log` |

配色测试包含各内置方案→经典的实际上下键选择、刷新、关闭重开、自动保存、自定义色及深浅往返；音频图层测试包含真实按钮、固定实例浮动、独立绘制像素、数据／视窗保持；吸附测试包含按下→移动→Alt→释放和指示像素；DSL 测试包含逐字符输入、实际浮层可见区域、主题像素、Tab／Enter 插入、Esc、失焦、IME；Dock 覆盖深浅、240 DIP 窄浮窗、多标签真实点击及单标签无残留线。

首轮 83 / 85 通过，剩余两个测试分别用了 ComboBox 未处理的 Home 键和没有 XAML NameScope 的浮窗根。修正为实际上下键及固定面板自身 NameScope 后整组通过；生产行为未因这两个 fixture 修改。首次尝试在分工编辑未完成时遇到编译错误，未计为测试结果或功能失败证据。原日志保留在 `timeline-polish-ui-before.log`。

已查看 `artifacts/verification/timeline-polish-ui/` 的浅色时间线、深浅 DSL 补全、多标签窄浮窗等渲染截图；测试自动关闭创建的窗口。Headless 真实输入和像素不代替跨 DPI、触控板手感或真实桌面的复杂停靠视觉验收。

Windows 更新目录为 `artifacts/verification/preview-win-x64/`，是自包含验证构建；本轮没有重打完整媒体发布 ZIP。操作说明在 [Quick Start](../quick-start.md)、[工作台手册](../workbench.md)、[DSL 文档](../effect-dsl.md) 与 [窗口基础](../workspace-windowing.md)。
