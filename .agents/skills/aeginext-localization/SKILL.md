---
name: aeginext-localization
description: Add, change, review, or debug AegiNext UI localization using external JSON language packs, the shared Localization service, and live Avalonia Loc bindings. Use for translated controls, dynamic view-model text, menus, window titles, language preferences, and language-resource packaging; subtitle content, user preset names, and effect-script syntax remain business data.
---

# AegiNext 本地化

先定位当前 AegiNext checkout，读取实际消费方及相关代码。仓库路径均相对 checkout 根目录；不要把开发机绝对路径写入实现。入口是 `src/AegiNext.Desktop/I18n/`，命名空间为 `AegiNext.Desktop.I18n`。本地化体系已统一，新增文案沿用此入口。

## 按改动选择上下文

| 改动 | 优先读取 |
|---|---|
| 文案、语言包、缺项或语言匹配 | `I18n/Languages/*.json`、`I18n/Localization.cs`、`I18n/LocalizationCatalog.cs`、`I18n/LanguagePackReader.cs` |
| 静态控件文本、工具提示、辅助名称 | 消费方 AXAML、`I18n/LocExtension.cs`、`I18n/XamlNamespace.cs`、`Controls/Common/IconText.cs` |
| 动态状态、菜单、Dock 或窗口标题 | 所属 ViewModel / 控制器、`Workspace/WorkbenchSession.cs`、`Windowing/WorkbenchWindowRegistry.cs` |
| 首次显示、语言选项、偏好 | `App.axaml.cs`、`Views/MainWindow.axaml.cs`、`Workspace/WorkbenchStartupPreferences.cs`、`Settings/Appearance/AppearanceSettingsViewModel.cs` |
| 构建或发布资源 | `AegiNext.Desktop.csproj`、`scripts/publish/AegiNext.Publish.psm1`、`docs/zh-CN/publishing.md` |

生产代码路径除完整路径外均在 `src/AegiNext.Desktop/` 下；仓库脚本与文档从 checkout 根目录解析。完整约定见 `docs/zh-CN/composable-workspace.md` 的本地化部分。需要验证时读取 [references/verification.md](references/verification.md)；不必为普通文案修改读取所有实现。

## 语言包与 key

修改源文件 `I18n/Languages/`，不要把输出目录当作源码。内置 `en-US.json` 和 `zh-CN.json` 的 key 集合保持一致；新增 key 同时提供两种文案。第三方语言允许缺项并使用回退。沿用来源前缀 `Workbench`、`Settings`、`Preview`、`Layout`、`Log`、`WorkflowLog`、`WindowChromeProbe`；新模块按真实所属职责命名，保留语义不同的 key，例如 `Workbench.Export` 与 `Settings.Export`。

```json
{
  "LanguageName": "简体中文",
  "LanguageID": "zh-CN",
  "Strings": {
    "Workbench.Cancel": "取消"
  }
}
```

此片段说明结构，不用于替换完整内置文件。保存为无 BOM 的 UTF-8。`LanguageName` 非空，`LanguageID` 为有效文化标识，`system` 是保留选择值；`Strings` 的 key 非空、值全部为字符串。拒绝重复根字段和文本 key。ID 比较忽略大小写并规范化，文本 key 区分大小写。翻译中的位置参数必须保留对应索引与格式语义。

启动只扫描语言目录第一层 `*.json`，以文件内 ID 为准。损坏的可选包记录文件诊断并排除；重复 ID 冲突组全部排除。有效且唯一的 `en-US` 缺失时启动失败。不要增加 C# 文案副本掩盖资源问题。资源修改或新增后重启生效，当前体系没有热重载。

工程字幕、用户预设名称、文件路径、脚本关键字与语法、序列化 ID 和编码器标识保留业务含义。枚举到文案 key 的映射放在所属业务模块，例如 `Editing/AnimationPropertyLocalization.cs`，不扩张通用本地化服务。

## 服务契约与偏好

- `Initialize(directory)` 在 UI 线程调用一次，捕获原始系统 UI 文化；应用在加载 AXAML 前用 `Path.Combine(AppContext.BaseDirectory, "i18n")` 初始化。不要在每个控件或窗口重复初始化。
- `Get(key)` 可在后台线程读取不可变快照，顺序为当前语言、`en-US`、原始 key。翻译结果不依赖调用线程的 `CurrentUICulture`；异步播放回调也使用此入口。
- `Format(key, params object[] arguments)` 使用 `CurrentCulture` 格式化数值。切换语言只同步界面文化，不因翻译改变现有数值输入文化。
- `SetLanguage(lang)` 仅在 UI 线程切换，支持已安装 ID 与 `system`。未知 ID 抛出参数异常并保持原语言；相同选择和生效状态不重复通知。
- `SelectedLanguageID` 是请求值，`CurrentLanguageID` 是解析结果；选择 `system` 时二者可以不同。系统匹配使用初始化捕获的文化，依次尝试精确 ID、父级、同语言候选，中文／英文优先 `zh-CN`／`en-US`，其他候选按 ID 排序，最后回退英文。
- `KnownLanguages` 供设置页显示 `LanguageName`、按 `LanguageID` 选择，加上翻译后的“跟随系统”。不要恢复中英文固定索引或偏好语言白名单。
- 服务不写偏好。设置页走现有偏好保存流程；主窗口在自身 AXAML 加载前应用同一份已读取的启动偏好，并把同一 store / snapshot 传入会话。已保存语言包暂时缺失时显示英文，保留原保存 ID 与其他偏好。
- 直接 `SetLanguage` 后，保存主题、音量等无关偏好不得重置当前语言；仅语言偏好本身变化时应用新选择。

## 静态文本使用 Loc

默认 Avalonia XML 命名空间已通过程序集 `XmlnsDefinition` 注册 `LocExtension`，直接使用无前缀的 `{Loc Key=...}`：

```xml
<TextBlock Text="{Loc Key=Workbench.Codec}" />
<Button Content="{Loc Key=Workbench.Cancel}" />
<Expander Header="{Loc Key=Settings.Advanced}" />
<Button ToolTip.Tip="{Loc Key=Workbench.Save}">
    <common:IconText Text="{Loc Key=Workbench.Save}" IconKey="Save" />
</Button>
```

最后一个示例需要 `xmlns:common="using:AegiNext.Desktop.Controls.Common"`。复用 `IconText` 的现有图标、字体和行高；图标身份由 `IconKey` 决定，不能从翻译 key 或翻译结果推断。根据控件属性迁移 `Text`、`Content`、`Header`、工具提示和辅助名称；不要覆盖仍需业务绑定的状态文本或动态内容。

`Loc` 返回可观察绑定，首次订阅立即提供当前文本，语言变化后重新推送。保留业务 `DataContext`；不要捕获目标控件、扫描控件树或重新引入翻译用 `Tag`。不要用一次性 `Localization.Get` 赋值替代需要即时更新的绑定。

## 动态文本与订阅生命周期

播放／暂停、静音、快捷键录入、导出状态等继续绑定所属 ViewModel，其刷新逻辑调用 `Get` / `Format`。状态保存语义 key、枚举和格式参数，不能仅缓存上一次翻译后的字符串。会话、布局控制器、窗口注册器和设置窗口集中刷新各自拥有的动态文本；先复用现有 `LanguageChanged` 处理入口，避免对同一对象重复订阅。

C# 创建的控件使用同一观察绑定，并持有、释放绑定句柄：

```csharp
var binding = button.Bind(
    ContentControl.ContentProperty,
    Localization.Observe("Workbench.Cancel").ToBinding());
```

这是 Desktop 程序集内部用法，`Observe` 目前是 internal。窗口在 `Closed` / `Dispose` 释放句柄和事件订阅；替换状态绑定先释放旧绑定。带格式参数的 provider 只捕获必要业务值，避免捕获整个窗口；参照 `Views/TrackStyleChangeDialog.cs`。固定面板临时 detach 与最终 disposal 要按现有生命周期区分，重新挂载不得失去翻译更新。

语言刷新只改变呈现，不能调用业务提交、重建工程、推进 Undo 或丢弃草稿。刷新本地化选项列表时保留稳定 ID、真实选中值和原始无效文本，禁止把 ComboBox 自动选择反馈当作用户编辑。相关模式见 `Panels/Styles/`、`Panels/Effects/` 的同步 `ChoicesRefreshing` / `ChoicesRefreshed` 和初始化保护；仅在需要的消费方复用该模式。

## 验证与交付

按改动范围执行 [验证参考](references/verification.md)，检查真实编译后的 AXAML、语言切换、业务状态和释放行为。key 存在性直接检查 JSON，不能用 `Get` 的回退结果证明翻译齐全。修改构建、加载或发布规则时检查实际输出 `i18n/`、包哈希与非程序工作目录启动。macOS 包路径为 `AegiNext.app/Contents/MacOS/i18n/`。

报告实际执行的编译／静态分析、测试与跳过、发布资源和原生验收证据。测试数量、语言 key 数量和既有失败均以当前执行结果为准，不把历史记录固定成验收标准。按钮图标、长文本布局和平台菜单的最终视觉验收与 Headless 结果分别说明。保持用户授权范围，保留无关工作区改动，不因使用此 skill 自动提交或升级依赖。
