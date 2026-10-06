# 可组合工作区、MVVM 与窗口外观

[English](../composable-workspace.md) | [简体中文](composable-workspace.md)

工作区重构完成批准的 Phase 1–4 实现；用户后续指令要求连续完成全部 Phase，取代逐 Step 等待确认。工作台与设置窗口参与迁移，独立 HDR 诊断窗口维持原入口和生命周期。工作区重构保留唯一会话与领域 Undo 栈；后续字幕轨道将当前项目升级为 v3，明确不支持 v1／v2。早期六面板、项目 v2 和兼容 v1 的记录属于历史阶段，不能作为当前读写规则。

## 空间、功能与子控件

| 目录 | 所有权和职责 | 边界 |
| --- | --- | --- |
| `Workspace/` | 显式组合根、唯一 `WorkbenchSession`、项目工作流、播放寻址、分析、导出与样式库协调 | 共享一个 `ProjectEditor`、媒体控制器、选择及 Undo；工作流可等待 |
| `Layouts/` | 主停靠树、标签、分割、浮动宿主、布局快照和个人预设 | 接收七个固定 View、稳定 Panel ID、草稿事务及手势取消回调；不拥有项目或任务 |
| `Panels/{Preview,Timeline,Subtitles,Styles,Effects,Export,Log}/` | 七个独立 View 与 ViewModel、本地输入适配 | ViewModel 提供状态和命令；View 只连接局部控件并提交时间、ID、颜色、变换等语义参数 |
| `Controls/Common/` | 标题栏等通用控件 | 不引用主窗口、Dock 或项目服务 |
| `Controls/Media/` | 视频帧呈现 | 呈现控件拥有和释放位图，业务 ViewModel 不持有位图 |
| `Controls/Editing/` | 时间线、画布、字体及数值草稿输入 | 绘制、命中和指针手势留在控件，编辑通过明确事件提交 |
| `Settings/{Appearance,Colors,Shortcuts,Styles,Effects}/` | 五页草稿、验证、命令和局部输入适配 | 设置窗口只组合页面；样式数值的原始无效输入可保留 |
| `Menus/`、`Windowing/` | 共享命令描述、各窗菜单投影、输入路由、平台装饰与按钮安全区 | 每窗独立原生菜单根，所有入口复用同一命令对象 |

依赖锁定为 Avalonia **12.1.3**、CommunityToolkit.Mvvm **8.4.0**、Dock Avalonia / Model.Mvvm / Fluent **12.1.0.6**。业务 ViewModel 不继承 Dock 类型，也不持有控件或窗口。主窗口只查找自己的标题栏和停靠宿主，不跨面板查找控件。

设置宿主保留导航模型，五个页面各接收自己的页面模型。页面在加载 XAML 前设置局部空 DataContext，避免初始化过程中继承宿主模型，再由宿主的编译绑定投影外观／快捷键／样式模型。`x:DataType` 不约束运行时数据上下文；只断言页面最终模型正确，会漏掉初始化期间被绑定引擎处理的类型转换异常。设置启动回归和原生探针均监听 FirstChanceException，覆盖这类异常。

每个工作台创建七个 View 与 ViewModel，停靠、隐藏、浮动和预设切换均复用这些实例。停靠拓扑使用薄适配模型；内容模板在安装布局之前准备，已构造面板及中间 `WorkbenchToolControl` 都采用立即呈现策略，防止标题容器再次把现成控件排入后台队列，导致冷启动正文为空。回归把继承的后台呈现延迟设为一天，仍要求首次打开立即挂载四个活动面板，不依赖 Loaded、计时器或切换布局。时间线绘制区使用实际可用空间，不强制最小高度；工具行保持紧凑，关键帧、片段和命中区域同步适配，避免挤到分割条。

## 布局与个人预设

首次启动包含四个只读布局：标准、打轴、特效、压制。标准保持原工作区关系：视频与属性在上，时间线居中，字幕在下。拖动标题或标签可重排、分割、合并及浮动；关闭面板后通过“视图”菜单显示并激活。

顶部“布局”菜单列出内置和个人预设，顶层标题始终为“布局”，不追加“已修改”；内部保留预设勾选与修改状态，提供保存、另存为、管理及恢复默认。预设按稳定 ID 执行，未为每个个人预设扩张命令枚举。保存只读内置项时打开另存为入口；个人项可显式保存、重命名和删除。删除当前个人项保留当前工作空间，将其作为标准项的修改状态，避免删除名称时意外切换工作区。

个人应用目录独立保存 `layouts.json`，记录版本、拓扑、分割比例、标签顺序、活动及隐藏面板、焦点和浮窗位置／缩放。当前布局合并自动写入，命名预设仅显式保存时更新。写入串行排队，通过同目录临时文件、刷盘和原子替换完成；批准关闭后等待最终布局写入，再释放会话。恢复浮窗时约束到当前显示器工作区，缺失显示器时回到可见区域。损坏文件保留诊断，恢复标准布局。

布局属于个人设置，切换项目不改变布局。布局操作不写项目、不增加 Undo；播放位置、选择、关键帧、时间线缩放／滚动和导出状态属于共享会话与面板模型。

## 草稿与生命周期

切换预设前先取消未完成手势，再验证全部待提交项目草稿与导出参数。字幕、样式和特效全部有效时通过一次项目事务提交；无效时保留原始文本、保持当前预设并激活错误字段所在面板。数值输入保存未解析文本，不能通过 NumericUpDown 的旧有效 Value 绕过验证。普通移动、隐藏和浮窗关闭只取消手势，容器脱离造成的失焦不提交草稿。

浮窗单独关闭隐藏其中面板，主会话和任务继续运行。主窗口统一执行未保存确认、等待项目工作流与最终写入，关闭设置／管理／浮窗，再释放媒体、分析、导出、偏好和呈现资源。关闭取消或保存失败保留会话与输入；资源释放可重复调用但实际只进行一次。

样式和特效参数的有效草稿立即更新共享画布与视频合成状态；项目编辑器与 Undo 历史仅在失焦、数值 Enter 或显式操作提交时改变。无效或未完成文本保留最后一次有效预览并继续可编辑；其他字幕或导出草稿不会阻断局部预览。字体搜索仍在原有确认边界提交。排队的预览和失焦回调拒绝已改变的目标、快照与已释放宿主。

## 菜单与窗口

项目名和未保存标记位于自建标题栏，并同步 `Window.Title`。原项目／设置按钮行已删除，设置通过共享菜单进入。主窗、浮窗、设置窗、管理窗和模态辅助窗使用统一窗口注册；窗口菜单只在主工作台显示，各注册窗口的 macOS 原生菜单根长期保留，焦点切换不清空。菜单和原生按钮安全区属于交互区域，其余标题区域支持系统拖动；窄主窗口通过菜单溢出保留入口；浮窗使用统一命令上下文。

macOS 默认使用系统菜单，外观设置中的菜单位置选项可即时切换全部已打开窗口并持久化。Windows 在主窗将窗口菜单合入同一标题行；子窗保留标题栏及原生按钮。两平台保留完整原生装饰和真正的系统按钮：macOS 使用客户区扩展与实测红绿灯安全区；Windows 通过公开 WndProc hook、自定义非客户区、DWM 和优先 `DwmDefWindowProc` 保留原生按钮。独立平台细节见 [窗口基础](workspace-windowing.md)。

macOS 按钮安全区和原生诊断需要 `IMacOSTopLevelPlatformHandle.NSWindow`。Rider 将该平台句柄接口标记为不稳定并给出 CS0618；当前 CLI 构建为零警告、版本已锁定，真实 macOS 探针验证其有效。升级 Avalonia 时必须复验该平台边界，本轮没有通过反射或全局诊断屏蔽隐藏这项提醒。

旧版完整快捷键数组迁移时保留改键和禁用项，再补新增命令；未知、重复、冲突和不完整旧数组继续拒绝。窗口、浮窗和设置各注册一次输入路由，快捷键录入与文本编辑优先；中文／英文即时更新内置名称、菜单和页面，个人预设名称保留原文。

## 本地化

`AegiNext.Desktop.I18n.Localization` 统一拥有语言目录与当前语言快照。应用在 UI 线程显式调用 `Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"))`，先初始化再加载应用 XAML；主窗口在加载自己的 XAML 前应用已经读取的语言偏好。`Get(string key)` 可在任意线程读取当前快照，`Format(string key, params object[] arguments)` 沿用 `CurrentCulture` 格式化数值。`SetLanguage(string lang)` 在 UI 线程选择已安装的 `LanguageID` 或 `system`，即时刷新已有界面；未知 ID 抛出参数异常且不改变当前语言。服务本身不保存偏好。动态 ViewModel、菜单和窗口标题在所属生命周期内订阅 `LanguageChanged`，关闭或释放时解除订阅。

静态文案通过无前缀 `{Loc Key=...}` 注入 `Text`、`Content`、`Header`、工具提示和辅助名称等属性。扩展返回随语言更新的可观察绑定，保留业务 `DataContext`。用于翻译的 `Tag` 和控件树扫描已移除。例如：

```xml
<TextBlock Text="{Loc Key=Workbench.Codec}" />
<Button Content="{Loc Key=Workbench.Cancel}" />
<Expander Header="{Loc Key=Settings.Advanced}" />
<Button ToolTip.Tip="{Loc Key=Workbench.Save}">
  <common:IconText Text="{Loc Key=Workbench.Save}" IconKey="Save" />
</Button>
```

最后一个示例需要在根元素声明 `xmlns:common="using:AegiNext.Desktop.Controls.Common"`。`IconText.Text` 接收同样的本地化绑定，`IconKey` 独立使用现有 `WorkbenchIcon` 图标 ID／别名，保留当前图标映射；修改翻译 key 不改变图标。播放／暂停和快捷键录入等状态文案继续绑定 ViewModel，由其调用 `Localization.Get`。

语言源文件位于 `src/AegiNext.Desktop/I18n/Languages/`，使用无 BOM 的 UTF-8 JSON，构建、测试和发布自动复制到输出目录的 `i18n/`。JSON 根对象包含非空 `LanguageName`、有效文化标识 `LanguageID` 和字符串字典 `Strings`；文本 key 使用来源前缀，例如 `Workbench.Export` 与 `Settings.Export`。设置页由 `KnownLanguages` 加上已翻译的 `system` 选项生成语言列表，显示 `LanguageName`，按 ID 选择和保存，不硬编码中英文索引。已保存的有效 ID 对应语言包暂时缺失时，保留偏好 ID，当前显示回退英文。

启动时只扫描目录第一层 JSON；修改或新增语言包需要重启，不提供热重载。`system` 使用初始化捕获的系统文化，按精确 ID、父级 ID、同语言候选匹配；中文／英文候选优先 `zh-CN`／`en-US`，其他候选按 ID 排序，最终回退 `en-US`。单条文案依次查询当前语言、`en-US` 和原始 key。损坏的可选语言包及重复 ID 冲突组排除并记录诊断，`en-US` 必须有效且唯一。语言刷新保留个人名称、字幕正文和待确认草稿；自动测试与包检查不能替代按钮图标、长文本布局和原生菜单的最终人工视觉验收。

## 验证和复测

### 字幕定位、时间线与特效画布

时间线关键帧拖动仅沿水平方向修改时间。鼠标纵向移动保留全部标量、向量和颜色分量，时间未改变时不产生编辑或撤销记录。关键帧数值通过特效面板输入框填写，草稿实时预览，确认时以一次事务提交。

主时间线的字幕、形状和图片片段支持系统选择修饰键切换及 Shift 范围选择。抓取已选片段主体会让整个选择同步水平移动，保留各自轨道、时长和相对时间；最早片段到达零秒时整组共同停止。移动允许经过其他字幕，最终与同轨字幕重叠时全部移动片段显示红色，松手恢复原位置。吸附以抓取片段的起止边缘为基准，排除整组移动成员。单选字幕继续支持跨轨拖动，边缘裁剪仍只编辑抓取的片段。点击展开字幕轨道的特效空白区域会选中该轨道。

时间线取得焦点时，Cmd/Ctrl+C 复制选择，Cmd/Ctrl+V 在当前鼠标时间及轨道粘贴，Delete 删除整批选择。复制冻结当前选中轨道及源轨顺序，粘贴把该轨道对齐鼠标目标轨道，其他字幕保留轨道间距；映射越界时整批拒绝。快捷键当次按当前视口投影鼠标落点，标题、标尺、时间线外及拖动期间无有效落点；主体右键菜单冻结打开时的选择、点击时间和轨道，也可在点击的字幕轨道创建两秒字幕。本工程内的副本保留完整内容、相对时间和合成顺序；最终字幕碰撞会拒绝整批粘贴或创建。移动、粘贴、创建及整批删除各形成一次 Undo。吸附、步进、频谱和波形属于全局个人设置，多窗口共享，切换项目及重启后恢复；默认依次为开启、关闭、开启、开启。

字幕显式位置由归一化 Anchor、归一化 Pivot 和像素 Offset 组成，坐标原点在画布左上，X 向右、Y 向下。Anchor 指定画布中的参考点，Pivot 指定实际字形包围盒中的轴心；例如 Anchor / Pivot 都为 `(0.5, 1)`，Offset 为 `(0, -40)`，将文字实际底部中点置于画布底部上方 40 px。定位不拉伸文字，未启用显式位置时沿用九宫格对齐与边距。开启显式位置会用实际排版测量补偿旧基线，保证现有字幕不跳动。

`Rendering/ProjectSceneRenderer.GetLayerGeometry` 为实际渲染、画布选择框及拖动提供共同的字形边界和父级变换。字体塑形用 glyph bounds 与 glyph positions 求并集，空格 advance 不扩张有墨迹文字的边界；空白字幕提供明确的逻辑编辑框。描边、阴影及模糊不改变定位轴心。`Editing/SubtitlePositionDraft` 保存原始数值草稿，`Controls/Editing/SubtitlePositionEditor` 供工作台样式面板和设置模板页复用，不引用 Dock 或会话服务。

位置随字幕样式模板一起捕获、套用、导入和导出，统一使用 `.aegistyles`；字体资源的原有哈希与预算检查继续生效。当前项目 `.aeginext` 使用 v3，v1／v2 明确拒绝；样式模板保留既有版本及自己的迁移规则。项目 v3 的向量和颜色旧分量按属性迁移，未知及重复字段继续拒绝。早期项目 v2／兼容 v1 的结论仅属于历史阶段。

特效面板的位置恢复仅清除位置动画、位移和运动路径，保留字幕 Anchor／Pivot／Offset；样式面板的自动位置操作恢复对齐排版。字幕列表支持系统选择修饰键与 Shift 范围选择，通过稳定 ID 在刷新后保留多选，并以一次事务合并实际选中的连续字幕。单选仍与下一句合并，末句禁用该操作；不兼容效果或不连续选择会明确拒绝，不产生部分修改。

时间线直接同时显示字幕／图层片段与展开轨道全部 Clip 已有属性的关键帧和插值曲线，不再切换字幕／特效标签。整段移动平移片段及关键帧的项目时间，边缘裁剪保留原曲线相位，显式拉伸缩放动画时间。关键帧内容时间限定为 `[max(0, AnimationOffset), AnimationOffset + End - Start]`；结束关键帧可位于右边界，但图层显示仍遵守半开区间。裁剪删除区间外的关键帧并补边界值，保存曲线子区间，使重复裁剪后的缓入／缓出保持原轨迹。

唯一视频预览按项目比例绘制底板、等比例视频背景及真实场景，再叠加路径、选择框和手柄；特效面板不再提供独立画布或图层列表。`VideoFrameSurface` 在呈现控件中拥有位图；画面编辑与特效面板观察同一共享编辑状态，不创建第二个解码器。每个合成预览帧关联同次转换的原始 SDR 背景，避免重复叠加字幕或使用迟到帧对应的错误背景。视频与项目比例不一致时使用可见留边，画布命中、预览和渲染共享项目坐标。

测试使用真实 `ProjectEditor`、可控媒体与对话框边界，覆盖联合草稿事务、Undo、保存失败、寻址竞态、取消和关闭释放。Headless 回归按 Panel ID 访问真实 View，显式持有会话、模型与宿主注册表，并自动关闭全部测试窗口；真实鼠标事件覆盖命令按钮与时间线命中。

导出通过 `IWorkbenchExportService` 构造注入。请求固定点击导出时的项目与参数；路径选择与编码共用防重入边界和可等待的 Completion。受控服务验证预设切换期间任务、参数与进度不变，取消后迟到进度不能回写，关闭等待任务排空后仅释放一次。

当前构建使用隔离目录，避免跨平台共享 `obj` 中旧的 NuGet 路径干扰：

```sh
dotnet restore AegiNext.sln --artifacts-path artifacts/workspace-build -p:RestoreLockedMode=true
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --artifacts-path artifacts/workspace-build --no-restore
dotnet artifacts/workspace-build/bin/AegiNext.Desktop/release/aegi-next.dll --workspace-probe-auto --workspace-probe-report artifacts/verification/workspace-macos-release.json
```

`WorkspaceProbe` 使用独立临时偏好目录，运行真实主窗、停靠浮窗和设置窗；验证首次内容呈现、预设 A→B→A 实例、布局与命名预设隔离、菜单模式、原生按钮存在和统一释放。它不读取用户项目，不覆盖用户设置，结束时关闭全部窗口并移除临时目录。报告中的 DockSamples 保留真实视觉树与面板挂载信息。

诊断设置启动可追加 `--workspace-probe-source-profile DIR`：仅将偏好与样式库复制到新的临时目录，不使用来源布局，不回写来源文件。探针等待样式库初始化后通过实际设置命令开窗，并访问设置页面；早期报告的三页验收只证明当时页面，当前设置有五页；报告记录复制状态、加载模板数、完整开窗异常及 `SettingsBindingExceptionCount`。设置模型相关 InvalidCastException 即使被 Avalonia 内部处理，也会计入失败并使探针返回非零退出码。探针通过不能证明用户所有项目都已验收。

自动测试、原生运行和人工验收分别记录于 [Checkpoint](README.md#实施与验收记录)。macOS 红绿灯点击、真实拖动／分割合并、全屏和多屏视觉体验仍需人工验收；Windows 按钮点击及最大化恢复已有另行记录的 Parallels 实机证据；Snap、跨 DPI 与多显示器仍需对应验收。运行探针和几何测试不能替代这些结论。
