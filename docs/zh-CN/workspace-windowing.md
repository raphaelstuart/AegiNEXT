# 可组合工作区：窗口基础与实施边界

[English](../workspace-windowing.md) | [简体中文](workspace-windowing.md)

本轮采用 Dock 与完整 MVVM，覆盖工作台和设置窗口，独立 HDR 诊断窗口保持现状。本文件保留 Phase 1 / Step 1 的平台窗口基础说明；目前已接入正式主窗、停靠浮窗和设置窗。完整空间与 MVVM 分层、预设和生命周期见 [可组合工作区](composable-workspace.md)。

## 分层

| 目录 | 职责 | 依赖边界 |
| --- | --- | --- |
| `Controls/Common/WindowTitleBar` | 显示标题、承载菜单、排除交互区域、应用系统按钮安全区域 | 不访问工程、Dock、菜单命令目录或平台原生 API |
| `Windowing/` | 选择平台策略、调整客户区尺寸、原生按钮区域、Windows 非客户区消息与释放 | 接收 Window 和标题栏；不持有工程或面板状态 |
| `Diagnostics/WindowChromeProbe*` | 独立组合三种窗口宿主，运行平台探针并写报告 | 不读取用户工程或偏好；不替代正式工作台会话 |

后续代码继续遵守已确认边界：`Workspace/` 持有唯一工作台会话和协调服务；`Layouts/` 管理空间与薄 Dock 适配；`Panels/` 组合七个固定面板（含 Log）；`Controls/{Common,Media,Editing}/` 承载通用或专用子控件；`Settings/` 拆分页面与 ViewModel；`Menus/` 和 `Windowing/` 提供共享菜单、输入与平台窗口策略。业务 ViewModel 不继承 Dock 类型，不持有控件或原生窗口事件。

当前已锁定 CommunityToolkit.Mvvm 8.4.0、Dock Avalonia / Model.Mvvm / Fluent 12.1.0.6，保留 Avalonia 12.1.3。

停靠空间通过 `WorkbenchDockTemplateCatalog` 包装实际 `ToolChrome`，最外层 `PART_WorkspaceFrame` 使用共享 `WorkbenchPanelCornerRadius`（10 DIP）、`PreviewSurface` 和 `PreviewBorder`，并裁剪到同一圆角。ToolChrome／ToolControl 模板内边框及业务面板根 Border 不重复绘制；活动和非活动标题使用相同主题背景，通过高亮文字、紧凑图标区分。ToolTabStrip 左右填充区域不绘制顶线，ToolTabStripItem 的普通／选中／悬停状态均不画边框；选中项只用主色文字与 14% 主色底色，高亮前后 Padding 相同。业务面板共享圆角资源，避免浮动时出现矩形描边包裹圆角内容。此处是 Dock 面板外观，平台原生窗口仍由下述标题栏策略负责；本轮实际深浅主题像素及浮动生命周期记录见[时间线与补全整理 Checkpoint](checkpoints/timeline-completion-polish.md)。

## 标题栏与客户区契约

`WindowTitleBar` 暴露 `Title`、`MenuContent`、`CaptionInsets`。平台层按真实按钮位置测量左右安全区域；菜单的实际矩形与按钮区域不属于拖动区域。剩余标题区域通过 Avalonia 的装饰角色或 Windows 的 `HTCAPTION` 实现系统拖动。

macOS 的 Avalonia 12.1.3 `WindowImpl.ChromeHitTest` 直接读取 `Renderer.HitTestFirst` 命中 Visual 自身的装饰角色，不采用通用输入根的祖先角色查找。因此完整透明标题背景直接设置 `TitleBar`，外层系统按钮安全区及菜单设置 `User`；仅给文字或根控件标记不能让所有空白处可拖动。UI 回归分别验证直接渲染命中与祖先命中，包括内边距、菜单间隙、空标题和左右按钮预留区。

`WindowChrome.Attach(Window, WindowTitleBar)` 将标题绑定到 `Window.Title` 并选择原生适配器。原生窗口保留 `WindowDecorations.Full`；没有使用 Avalonia 仿制的最小化、最大化或关闭按钮。

主工作台、浮窗和设置等可调整大小的宿主使用 `SizeToContent.Manual`。未保存确认与布局命名对话框使用固定宽度、`SizeToContent.Height`，内容（包括自建标题栏）决定高度；长消息换行时高度随内容增长。标题栏适配保留宿主的尺寸策略，Windows 不为自动高度对话框回填手动恢复尺寸。程序化客户区尺寸通过返回的 `IWindowChrome.ResizeClient(Size)` 提交，尺寸必须为有限正值。Windows 去除原生 caption 后的外框与客户区换算集中在这个入口，避免继续直接写 Width / Height 引入边框漂移。最小化／最大化期间调整恢复尺寸，全屏期间延后到恢复普通状态。关闭或显式 Dispose 解除事件和原生 hook，已排队刷新不再访问释放后的窗口。

### macOS

使用 Avalonia 完整系统装饰及客户区扩展，将自建标题栏纳入同一窗口顶部。保留真实 NSWindow 红绿灯；通过 `standardWindowButton:` 获取三个系统按钮，测量其安全区域。Objective-C CGRect 返回区分 arm64 的 `objc_msgSend` 和 x64 的 `objc_msgSend_stret`。

macOS 默认使用系统菜单，切换后仅主工作台在标题栏同一行显示窗口菜单。浮窗、设置及辅助/模态宿主继续注册共享原生菜单，焦点切换不清空或替换已导出的根。完整工作台已通过外观设置、个人偏好和共享命令目录接入此策略；实际 NSApplication.mainMenu 验收见[本轮 Checkpoint](README.md#实施与验收记录)。

### Windows

保留完整原生窗口 styles，关闭 Avalonia 客户区扩展。使用公开 `Win32Properties` hook 处理 `WM_NCCALCSIZE`、客户区尺寸和边缘缩放；通过 DWM 扩展框架保留真正的系统按钮。`DwmDefWindowProc` 优先处理原生按钮命中和非客户区鼠标消息。

自动高度窗口在内容布局完成后，以内容期望尺寸、Padding、边框和最小／最大限制计算客户区，通过同一原生尺寸换算入口更新外框，保留 `SizeToContent`。尺寸比较按物理像素进行、刷新合并提交；内容已贴合时不重复调整。这样不会把原生 caption 留白额外加在自建标题栏之外，运行中消息变长或变短也能增高和收缩。macOS 与 Windows 的实际尺寸记录见[预览与自动高度 Checkpoint](README.md#实施与验收记录)。

按钮区域来自 `DWMWA_CAPTION_BUTTON_BOUNDS`，标题栏动态预留右侧空间。透明窗口合成及整个 Window 的绘制裁剪为原生按钮保留实际安全区，避免不透明客户区覆盖。最大化后 DWM 可能返回客户区命中，补充读取 Windows 自身的 `TITLEBARINFOEX` 三个系统矩形与可用状态。DPI 与框架变化合并到 UI dispatcher 刷新；原生错误进入探针报告。x86 与 x64 分别调用 `GetWindowLongW` 和 `GetWindowLongPtrW`。

`WM_GETMINMAXINFO` 仅调整跟踪尺寸，保留系统预填的 MaxSize / MaxPosition，使窗口管理器继续执行副屏适配。`WM_WINDOWPOSCHANGING` 继续交给 Avalonia；这条消息路径通过 Avalonia 12.1.3 源码与 Win32 文档复核，尚无 Windows 运行证据。

Windows 几何及互操作测试可在 macOS 执行，但不能证明系统按钮已经正确绘制或接受点击。2026-10-05 已在 Parallels Windows 11 上通过三个宿主的真实鼠标最小化、最大化、恢复、取消关闭及最终关闭，并保留实际截图。Snap、跨 DPI 与多显示器工作区仍需复杂实机验收；完整证据边界见[本轮 Checkpoint](README.md#实施与验收记录)。

## 独立诊断入口

诊断入口组合主窗、浮窗和设置窗三个模拟宿主。它们使用相同窗口基础，尚未接入真实业务窗口。自动模式会关闭全部探针窗口并结束独立进程；交互模式在关闭主窗时关闭其余窗口并写报告。

```sh
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj \
  --configuration Release --artifacts-path artifacts/window-chrome-build \
  -p:RestoreLockedMode=true

dotnet artifacts/window-chrome-build/bin/AegiNext.Desktop/release/aegi-next.dll \
  --chrome-probe-auto \
  --chrome-probe-report artifacts/verification/window-chrome-auto.json

dotnet artifacts/window-chrome-build/bin/AegiNext.Desktop/release/aegi-next.dll \
  --chrome-probe \
  --chrome-probe-report artifacts/verification/window-chrome-interaction.json
```

自动模式包含重复扩大／恢复客户区、全窗口菜单模式切换、最大化／恢复、取消一次关闭和最终释放。每次采样记录实际客户区、RenderScaling、装饰状态、菜单模式、原生按钮测量及错误。报告始终单独标记 `NativeInteractionStatus = "Requires manual verification"`；自动状态变化不等于系统按钮点击、拖动或视觉验收。

`--chrome-probe` 与 `--hdr-probe` 互斥。正常启动继续进入既有工作台，独立 HDR 诊断路径保持原有行为。

## 人工验收

macOS 需要在三个宿主中确认红绿灯可用、标题空白处拖动、菜单点击不会拖窗、系统／窗口菜单互切、最小化／恢复、全屏／退出全屏以及关闭取消。窄窗的完整菜单溢出与业务关闭确认在正式窗口接入时验收。

Windows 需要确认 DWM 系统按钮绘制、悬停、点击与 Snap、标题拖动、八方向缩放、最小尺寸、最大化／恢复、不同位置任务栏与副屏工作区、跨 DPI，以及最小化和最大化期间的程序化恢复尺寸。

复杂视觉部分由用户提供截图或实机验收；自动测试与平台人工结论分别记录在 [Checkpoint](README.md#实施与验收记录)。

## 参考

- [Microsoft 自定义窗口框架](https://learn.microsoft.com/en-us/windows/win32/dwm/customframe)
- [Microsoft DwmDefWindowProc](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmdefwindowproc)
- [Microsoft GetWindowLongPtr](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongptrw)
- [Microsoft MINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-minmaxinfo)
- [Avalonia macOS WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/native/Avalonia.Native/src/OSX/WindowImpl.mm)
