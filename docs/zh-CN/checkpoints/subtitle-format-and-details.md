# 字幕格式交换与统一详细编辑器

[English](../../checkpoints/subtitle-format-and-details.md) | [简体中文](subtitle-format-and-details.md)

日期：2026-10-05。本记录覆盖内容模型、事务、持久化、共享渲染、格式交换、详细编辑和范围试听的实现与自动验证。详情使用现有 Dock 工作区子面板，支持浮动、贴靠、隐藏和复用；布局 v3 读取 v1／v2，并把新增详情面板初始隐藏。

格式菜单递归提供 Aegisub／SRT 导入导出。旧命令数值和已配置 SRT 快捷键保持语义。字幕列表显示正文和派生类型。富文本、卡拉 OK、高级代码共用内容与草稿，富文本页也能选中和编辑已有片段。高级代码程序回填与异步 TextChanged 用户输入分别判断，避免回填时量化原有精确字时间。

富文本控件使用 Avalonia 原生文字／IME 输入、共享排版几何与局部位图。光标、选区、删除、预编辑下划线遵循完整字素边界。卡拉 OK 手势冻结时间投影，所有取消入口释放指针捕获，释放时只提交一次。全工作区草稿先准备再原子提交；无效代码或时间阻止切目标、切页、关闭和保存，必须修正或明确恢复。原始正文不能无损投影为 ASS 时，仅禁用高级字段。范围试听使用共享 Controller，通过播放所有权和 revision 防止旧任务重新启动，异步清理完成后再次检查 revision。

## 已执行的内容、格式、渲染与播放验证

统一模型保留规范正文，以字素安全的 UTF-16 范围保存局部样式，卡拉 OK 片段具有稳定身份及逐字精确时间。文字增删按受影响区域重新分配时间；拆分、合并、整行样式应用和选区样式均可撤销。整行样式应用清除局部覆盖并保留卡拉 OK。工程 v4 在内存中读取升级 v3，样式库 v3 读取 v1／v2；加载不改写源文件。

预览、详细编辑和实际导出 worker 共用多样式排版、基线、字素命中、装饰和高亮。系统字体按完整字素回退，相邻同字体合并塑形；中文、Latin 和 ZWJ emoji 已在 macOS 实际验证。明确工程字体的缺字继续报告诊断。缓存与关闭释放全部字体和 shaping 资源。

最终定向集合均通过、无跳过：

| 集合 | 通过数量 | 验证重点 |
|---|---:|---|
| Core | 80 | 内容类型、字素范围、项目验证、样式与动画求值 |
| Application | 187 | 旧文件升级、局部时间重分配、Undo/Redo、拆分合并、ASS/SRT、高级代码源映射及整批导入 |
| Rendering | 113 | 多字体、多样式、卡拉 OK 三种高亮、命中与缓存释放 |
| Media 会话 | 25 | 有范围的音视频末尾、精确时间、暂停后尾音排空 |
| Desktop Controller | 24 | 循环所有权、主定位、取消开始、故障关闭和旧异步请求 |
| 实际 worker 与传输 | 6 | 工程字体、下划线／删除线、卡拉 OK 快照、中文／ZWJ 系统字体及真实 H264 压制 |

Core、Application 与 worker 的最终结果分别保存在 [Core TRX](../../../artifacts/verification/subtitle-core.trx)、[Application TRX](../../../artifacts/verification/subtitle-application.trx) 和 [worker TRX](../../../artifacts/verification/subtitle-worker.trx)。上述集合不累加先前分阶段重跑的数量。

最终 Desktop 构建同时构建导出 worker，编译警告与错误均为 0。本机从现有依赖构建了 macOS arm64 的解码、音频、导出和原生渲染库，并执行真实 worker；原生依赖包含面向 macOS 27 的库，链接时存在依赖部署目标高于项目 macOS 14 目标的提示，因此不据此宣称旧版 macOS 已通过。没有升级 .NET、Avalonia 或引入商业富文本依赖。

## 已执行的 Desktop 验证

Desktop 定向测试 **64 通过，0 跳过**，筛选表达式：

```text
FullyQualifiedName~SubtitleDetails|FullyQualifiedName~SubtitlePreedit|FullyQualifiedName~SubtitleFormat|FullyQualifiedName~WorkbenchLogShortcutMigration|FullyQualifiedName~WorkspaceLayoutMigration|FullyQualifiedName~WorkspaceLayoutValidator|FullyQualifiedName~WorkbenchPreferences|FullyQualifiedName~SubtitleRow|FullyQualifiedName~WorkspaceDraft
```

其中最终聚合 63 项，随后单独执行新增类型列用例 1 项。覆盖事务与撤销、精确时间、严格 UTF-8／BOM、独立重叠轨道、转换取消、旧快捷键／布局迁移和草稿冲突。**此计数不含 Controller 播放测试。**

Avalonia Headless UI **36 通过，0 跳过**，筛选表达式：

```text
FullyQualifiedName~SubtitleDetailsEditing|FullyQualifiedName~WorkbenchWindowRegistryUi|FullyQualifiedName~WorkbenchLayoutsUi|FullyQualifiedName~NumericDraftEditing|FullyQualifiedName~LocalizationUi
```

实际执行中文＋ZWJ emoji 的 KeyTextInput、原生 IME client 协议的代理对内部光标、工具栏保留选区、富文本页片段属性、无效代码的切行／切页／浮窗关闭阻塞、真实轴拖拽一次撤销、Esc 释放指针捕获、Float→Dock 持久化与复用，以及本地化和菜单投影。测试结束关闭宿主，清理时明确放弃测试留下的无效草稿。

两个项目编译均无编译警告或错误。新增编辑器、协调器、投影、格式工作流、诊断对话框、递归原生菜单和布局迁移的 Rider 检查无普通 WARNING／ERROR。原有控件／宿主的命名、默认参数与可空判断样式提示保持原状。中英语言包各 525 个 key，集合一致；`git diff --check` 通过。

## 原生与人工验收边界

现有 `--workspace-probe-auto` 实际执行了真实详情浮动子窗口、中文／ZWJ 输入显示、三切页不产生新事务、同一视图贴靠、贴靠布局保存和关闭再开复用六项检查，**新增六项全部通过**。最后焦点诊断运行共通过 355 项检查，Settings、Export 与 GetBinding 异常计数均为 0。偏好使用临时 `AegiNext.Workspace.Probe/<guid>` 独立目录，结束后删除。

[原生焦点报告](../../../artifacts/verification/subtitle-details-native-focus.json)保留三个菜单空闲样本失败。诊断记录证明这些样本 `ApplicationIsActive=false`，前台为 `com.netease.uuremote`／UU远程，PID 7748，探测进程 PID 为 12395；失败发生时另一应用夺取了焦点。没有放宽断言，没有修改远程控制应用。完整菜单空闲验收仍需在不受抢焦点干扰时复核。

Headless 输入与程序原生探测不能替代真实 macOS 输入法候选窗口、指针贴靠、字体外观和可听循环的人工验收。本阶段 Windows／Linux 交互及跨屏行为仍需分别验证。操作与 ASS 支持边界见[使用说明](../subtitle-editing.md)。


## 2026-10-06 详情交互修订

详情的属性改成完整字段组，灰色标题在上、输入在下，阴影偏移的 X/Y 标签保持在轴输入左侧。字段按组换行，独立属性滚动区给正文保留空间，数字输入隐藏微调按钮。正文选区样式与卡拉 OK 高亮分别持有草稿；高亮仅编辑填充、描边和阴影，套用高亮预设不会引入字体资源或改变排版。样式面板的卡拉 OK 设置迁入详情，生成、清除、拆分和合并按钮移除，以“启用卡拉 OK”开关统一管理逐字时间；关闭保留高亮配置。

可编辑片段统一为完整字素，导入与高级文字编辑保留原区域时长、样式和精确时间。超出整句结束的正时长可提交，轴右侧红框显示可选择和继续拖拽的溢出区域，整句起止不变。“吸附”默认开启，对齐 10 毫秒网格、手势开始时的原片段边界和字幕结束线，Alt 临时关闭；该开关不产生工程事务。高级代码不生成工程位置标签，手动 `\pos` 返回内容页位置诊断；文件 ASS 的静态位置交换保持支持。

顶部改为单个段内播放／暂停按钮和独立循环开关，循环默认关闭；选中片段时播放该片段，否则播放整句。播放中切换循环即时生效，暂停中切换不会发声；失焦继续暂停。范围自然结束后按钮恢复播放文字，语言切换不启动播放。恢复操作显示为“重置”。

本轮 Desktop 独立草稿、颜色及位置定向集合 **19／19 通过，0 跳过**，结果见 [draft TRX](../../../artifacts/verification/details-revision-draft.trx)。筛选表达式：

```text
FullyQualifiedName~SubtitleKaraokeStyleDraft|FullyQualifiedName~ColorDraft|FullyQualifiedName~ColorInputMode|FullyQualifiedName~SubtitlePositionDraft|FullyQualifiedName~SubtitlePositionEditMode
```

最终 Headless UI 定向集合 **79／79 通过，0 跳过**，结果见 [UI TRX](../../../artifacts/verification/details-revision-ui.trx)。包含 root 新增吸附与溢出轴真实指针、自然播放结束按钮恢复、Loop 开关不自动播放、正文和高亮独立草稿、字体资源不导入、中文／ZWJ／IME、浮动贴靠、窄字段组、明暗颜色禁用几何、旧样式面板、全局窗口和语言回归。筛选表达式：

```text
FullyQualifiedName~SubtitleDetails|FullyQualifiedName~KaraokeStylePreset|FullyQualifiedName~ColorInputDisabledGeometry|FullyQualifiedName~KaraokeAxisOverflow|FullyQualifiedName~KaraokeAxisSnapping|FullyQualifiedName~NumericDraftEditing|FullyQualifiedName~LocalizationUi|FullyQualifiedName~StylesPanelLayout|FullyQualifiedName~ColorDraftInput|FullyQualifiedName~ColorInputBar|FullyQualifiedName~SubtitleFontEditing|FullyQualifiedName~SubtitleInputTypography|FullyQualifiedName~WorkbenchWindowRegistryUi|FullyQualifiedName~WorkbenchLayoutsUi
```

以上两个集合分别记录，不重复累加首轮重跑。Desktop、worker 和 UI 构建的编译警告／错误为 0；详情与样式面板、高亮草稿和新增测试的 Rider 普通 WARNING／ERROR 为 0，中英文各 539 个 key、集合一致，`git diff --check` 通过。中文截图位于 `artifacts/verification/details-revision-{light,dark}-{950,460}-{fields,disabled,highlight}.png`，共 12 张；已复核字段与颜色没有交叠，正文区域保留空间。截图用于代码生成布局复核，仍需用户在真实应用中验收字段、原生输入法和可听播放。


“启用卡拉 OK”经截图复核移至顶部播放／循环控件之后，宽窗首屏可发现，窄窗随标题区整体换行。该最后布局调整仅重跑相关 **9／9**，见 [最后布局 TRX](../../../artifacts/verification/details-revision-ui-final-layout.trx)，筛选为 `FullyQualifiedName~SubtitleDetailsFieldsUiTests|FullyQualifiedName~KaraokeStylePresetUiTests`。这 9 项是 79 项集合的交集，不累加为 88 项。

本轮其他层最终验证：Application 全集 **243／243**、Rendering 溢出区间裁剪新增项 **1／1**。吸附的网格、结束线和原邻居边界先出现 3 项真实红灯，实施后 snap＋overflow 实际指针 **7／7**，原始 [red TRX](../../../artifacts/verification/karaoke-snap-red.trx) 与 [green TRX](../../../artifacts/verification/karaoke-snap-green.trx)保留。

Root 的 Desktop 范围／协调器定向集合初轮 65 项中 64 项通过、原生开关项未执行；启用原生验证后同一用例 **1／1** 通过，最终该集合 **65 项均已执行通过**。分别保留 [Desktop TRX](../../../artifacts/verification/details-revision-desktop.trx) 和 [原生预览补跑 TRX](../../../artifacts/verification/details-revision-native-preview.trx)，不把补跑当作新增第 66 项。范围循环开关、自然结束通知和旧异步取消已覆盖。实际 worker／传输 **4／4** 通过、无跳过，包含 5 次实际视频导出，结果见 [worker TRX](../../../artifacts/verification/details-revision-worker.trx)。这些本轮集合与 2026-10-05 基线重叠，分别记录，不跨日期累加。

## 2026-10-06 第二次交互修订

详情收敛为**富文本**与 **AegiSub代码**两个切页。界面统一使用“高亮”，已有本地化 key 和持久化的卡拉 OK 枚举身份保持不变。启用高亮后，逐字轴显示在富文本正文下方。选中片段打开弹出编辑器，包含吸附、片段时长、句前留白和高亮方式；常驻“当前片段”显示及独立时间属性区移除。正时长片段可超出句尾，溢出部分仍可编辑，字幕整句边界不变；时间轴吸附和 Alt 临时关闭继续保留。

顶部“高亮样式”开关切换共享样式区的编辑目标：正文模式编辑正文选区，高亮模式仅编辑颜色、描边和阴影，保留正文的字体、字号、文字装饰和排版，并禁用对应控件。共用预设选择器在高亮模式显示高亮预设，选中即套用。虽然共用控件，正文选区和高亮草稿仍分别持有；不再提供独立高亮参数区或“应用高亮”操作。

三个图标动作分别将正文预设应用到选区、应用编辑后的正文选区样式、清除选区局部样式，只作用于正文选区，高亮模式下不可用。播放／暂停保持单个图标按钮，旁边保留循环、启用高亮和高亮目标开关。顶部与样式工具栏固定，属性组可折叠，正文和代码区可滚动。窄窗继续按完整字段组换行，X/Y 向量输入保持同行。

合法高亮字段按 Enter 或失焦提交，非法原始输入保持可编辑草稿，Esc 恢复该字段；选中预设无需额外点击“应用”。片段时长必须为正，句前留白不得小于 0。“重置”恢复详情待提交草稿。共享样式目标切换及切页继续遵循原有验证边界，不会丢弃非法草稿。

本轮 Desktop 模型／工作流定向集合 **29／29 通过，0 跳过**，覆盖正文与高亮草稿、详情布局迁移、高亮编辑协调及新增 9 项参数用例，见 [model TRX](../../../artifacts/verification/details-highlight-model.trx)。最终 Headless UI 回归集合 **69／69 通过，0 跳过**，其中包含 28 项详情用例，以及吸附、溢出、禁用颜色几何、布局、窗口、本地化和数字草稿，见 [UI TRX](../../../artifacts/verification/details-highlight-regression.trx)。中英文语言包各 546 个 key，集合一致。

补强回归覆盖四项交互保护：直接调用真实 `Popup.Close` 时，非法草稿取消关闭并保留同一弹窗根、Child、焦点和播放；保存验证精确聚焦到非法样式字段；被拒绝的高亮预设选择恢复下拉框原有显示；选中片段立即刷新工具栏动作。真实关闭用例先出现 **1／1 失败**，保留 [popup-close red TRX](../../../artifacts/verification/details-highlight-popup-close-red.trx)，修复后已包含在最终 69 项通过集合中，红灯运行不累加进绿灯数量。[片段弹窗截图](../../../artifacts/verification/details-toolbar-clip-popup.png)展示固定工具栏、两个切页、同行 X/Y 输入和逐字轴上方的弹出编辑器。目视复核确认弹窗四项输入按 2×2 排列、文字清晰，选中首字后正文样式动作同步可用。

这些定向集合与历史记录重叠，不累加此前数量。本次没有重跑整套内容和媒体测试。最新 Rider 检查的 5 个改动核心文件无普通 WARNING／ERROR，WEAK WARNING 风格建议仍保留；交互修复后的最终 Desktop 与 worker 独立构建成功，编译警告／错误为 0，`git diff --check` 通过。Headless 覆盖与截图不能替代新弹出编辑器、原生输入法候选窗口和可听播放的原生／人工验收。
