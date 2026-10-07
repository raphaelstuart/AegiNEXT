# 工作区集成

[English](../en/composable-workspace.md) · [简体中文](composable-workspace.md) · [全部指南](README.md)

## 按职责落位

下列目录均在 `src/AegiNext.Desktop/` 中。

| 目录 | 职责 |
|---|---|
| `Workspace/` | 唯一 WorkbenchSession、编辑器/控制器、选择、草稿和可等待工作流 |
| `Layouts/` | Dock 树、浮窗、布局快照和预设 |
| `Panels/` | 功能 View 与 ViewModel |
| `Controls/Common/`、`Controls/Editing/`、`Editing/` | 共享控件及字段/手势编辑行为 |
| `Settings/` | 个人库/偏好和对应草稿 |
| `Views/`、`Windowing/` | 宿主、平台外观、菜单和输入协调 |
| `I18n/` | 语言服务、JSON 语言包和实时绑定 |

新增 API 前先阅读使用面板与最近的共享控件。共享控件暴露值、草稿、命令和完成/取消事件，不主动取得会话、不依赖 Dock；ViewModel 保存业务状态而非控件/像素。

## 接入面板或控件

1. 在功能目录新增 View/ViewModel，在 Workspace 组合依赖。
2. 按稳定 `WorkbenchPanelIds` 为布局控制器提供固定 View，停靠/隐藏不重建实例。
3. 实际输入通过共享命令路由，保留文本输入与 IME 行为。
4. 切换编辑目标前提交有效草稿；无效原文保持原目标，Esc 只恢复一个字段。
5. 一次完成的手势提交一个事务；捕获丢失或目标变化时取消。关闭时等待异步工作并释放自有资源。

布局只管理空间，不拥有编辑器、Undo、播放或导出。当前布局和命名预设独立；持久化校验应用自有快照并原子写入，恢复浮窗时限制到可用显示器。关闭浮窗隐藏面板，关闭主窗口刷新布局并等待会话结束。

## 窗口与输入

复用字体、间距、面板圆角和窗口外观；布局绘制外框，面板避免重复边框。为浮窗/设置宿主注册相同菜单与快捷键策略。

macOS 默认系统菜单，窗口菜单模式只在主工作台显示菜单。Windows 外观接入系统标题栏动作和缩放。主题/快捷键更新保留命令身份，语言重组在安全菜单生命周期进行。

## 本地化

外部 UTF-8 JSON 语言包位于 `I18n/Languages/`，复制到应用 `i18n/`。包含 `LanguageName`、`LanguageID` 和字符串值的 `Strings`，语言 ID 与键不能重复。

- C# 使用 `Localization.Get("Key")` 和 `Localization.SetLanguage(...)`。
- XAML 使用 `{Loc Key=...}` 实时绑定。
- 查找回退：当前语言 → `en-US` → key。
- 自动发现语言包，保留系统语言匹配和个人偏好。
- 字幕内容、预设名称与 DSL 语法保持业务数据。

不添加 Tag/树扫描。语言包修改后重启，发布时校验并记录哈希。可移植规范见[控件](../../.agents/skills/aeginext-controls/SKILL.md)和[本地化](../../.agents/skills/aeginext-localization/SKILL.md) Skill。

## 验证接入

运行受影响的 Desktop 控制器测试和 Desktop.Ui Headless 测试，使用真实 View、指针/按键输入及可控服务边界，并关闭所有测试宿主。原生菜单、拖拽/停靠、全屏、多屏、DPI 与 IME 外观需对应平台交互检查。
