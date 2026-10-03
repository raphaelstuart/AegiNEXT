# 工作区布局边界

`WorkbenchLayoutController` 只拥有工作区空间。它不拥有工程、媒体控制器、Undo 栈、业务 ViewModel 或导出任务。

- 六个稳定 Panel ID 定义于 `WorkbenchPanelIds`。组合根提供六个固定 View 实例，`WorkbenchDockPanel` 仅连接稳定 ID、标题与 View。
- `WorkbenchDockFactory` 和 `WorkbenchDockSnapshotCodec` 是 Dock 的薄适配层；业务层不传递 Dock 类型。
- `WorkbenchDockPanelTemplate` 总是返回已提供的 View。切换预设或重新停靠不重新创建 View、ViewModel 或工程会话。
- 工作区根、分割、标签组和面板适配实现 Dock 的 `IDeferredContentPresentation`，声明不延迟呈现：六个 View 已由组合根创建，挂接时立即 materialize，不依赖分帧创建或计时器才能收到真实输入。
- Tool 标签的中间呈现控件 `WorkbenchToolControl` 也声明不延迟呈现。空间模板保留 Dock 的跟踪、标题、菜单、标签与拖拽行为，避免冷启动时 ToolChrome 将现成的 ToolControl 排入后台队列导致正文短暂为空。
- `WorkbenchDockTemplateCatalog` 在宿主设置布局前安装全部空间模板。首次创建窗口时，根、分割和标签模板已可用，避免模板应用过程中添加目录造成初始布局退化为文本占位。
- `Controls/Common` 等子控件不引用此目录。空间操作只取消指针手势；草稿提交仅在切换预设前调用注入的统一事务边界。

## 宿主集成

构造函数接受主 `Window`、稳定 ID 到 `Control` 的映射、个人设置目录、`Func<bool>` 草稿事务、取消手势的 `Action`、以及注册新窗口的 `Action<Window>`。将 `Host` 放入主窗口正文。注册回调负责统一标题栏、菜单与输入路由；布局目录不创建第二份命令目录。

`FloatingWindowTitleChanged` 提供当前活动面板标题，宿主协调器可以结合工程标题更新系统 `Window.Title`。浮动宿主继承 Dock 的 `HostWindow` 保留停靠输入能力，同时使用 Avalonia `Window` 样式键，避免 Dock 的窗口主题替代系统按钮。

批准主窗口关闭后先 `await FlushAsync()`，再 `Dispose()`。`Dispose()` 关闭浮窗及布局管理窗口、解绑观察和计时器。浮窗单独关闭将其中面板移入隐藏集合，不释放业务会话。隐藏后重新打开优先恢复当前会话中仍存在的原标签组，原组不可用时重新打开到主工作区。

## 持久化

个人目录中的 `layouts.json` 保存应用自己的版本 1 快照，不序列化 Dock 对象。快照记录分割比例、方向、标签顺序、活动面板、焦点面板、隐藏 Panel ID，以及浮窗物理位置、逻辑尺寸和显示缩放。

当前布局通过 UI 线程上的合并计时器写入。文件写入串行排队，使用同目录临时文件、刷盘和原子替换；关闭前等待最终写入。当前布局和命名预设属于不同字段，自动记忆不会更新命名预设。四个内置布局只读，用户显式另存为后可保存、重命名和删除。

恢复前验证版本、面板完整性、重复 ID、节点深度、比例、活动标签和浮窗尺寸。损坏或超出文件长度限制时保留诊断副本，恢复标准布局。显示器改变时以当前工作区和缩放重新约束浮窗；坐标保留负值以支持左侧屏幕。

## 验证

纯测试位于 `Tests/AegiNext.Desktop.Tests/Layouts/`，覆盖快照拓扑、四个只读预设、当前布局与预设隔离、排队原子写入和失败恢复、损坏诊断及显示器约束。`WorkbenchLayoutsUiTests` 覆盖固定实例、切换前草稿拒绝、隐藏草稿保留、预设管理、浮窗关闭和语言切换。

`MainDockContentUiTests` 直接检查真实主窗口首次标准布局的四个活动 View、局部 ViewModel、字幕输入、时间线及特效按钮的视觉附着。模板目录在设置根布局前完整安装，不依赖用户切换预设触发首次内容创建。

`InitialWorkspaceMaterializesWithoutTheDeferredPresentationQueue` 为宿主继承的延迟呈现设置一天等待，用于确认根、分割、ToolChrome 中间控件和面板仍同步挂接；此用例不修改 Dock 的全局调度设置，不通过额外布局切换或 Loaded 重置掩盖冷启动竞态。

macOS 原生 `WorkspaceProbe` 已验证首次标准布局的四个活动 View、预设轮换、固定实例、浮窗关闭和重新打开，以及三类窗口的原生红绿灯和菜单模式切换；`DockSamples` 记录真实视觉树、尺寸及局部 ViewModel。报告位于 `artifacts/verification/workspace-macos-debug.json`，此结果与 Headless 回归分开记录。

实际拖拽合并标签、跨屏与跨 DPI、原生按钮点击和全屏仍需桌面交互验收；几何测试和原生窗口属性检查不替代这些手势证据。Windows 原生窗口按钮需要 Windows 实机验收。
