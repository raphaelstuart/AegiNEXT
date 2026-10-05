# 本地化验证参考

路径相对当前 AegiNext checkout。选择受影响的测试；没有新故障或未解决疑点时，不重复整套回归。下列类名是现有入口，运行前确认仍存在，避免零测试被误报为成功。

## 按改动选测试

| 改动 | 测试入口与断言 |
|---|---|
| JSON、加载器、回退、系统语言 | `Tests/AegiNext.Desktop.Tests/LocalizationCatalogTests.cs`：第三语言发现、元信息、重复字段 / key / ID、无效英文包、缺项回退、未知 ID、系统匹配 |
| 文案消费方与映射 | `WorkbenchLocalizationTests`、`SettingsLocalizationTests`、相关 `EffectScriptLanguageTests` / `ColorDraftTests` |
| AXAML、工具提示、辅助名称、模板、上下文菜单、图标 | `Tests/AegiNext.Desktop.Ui.Tests/LocalizationUiTests.cs` 与 `LocalizationFixtureView.axaml`：先断言当前文案，再切换并断言更新；同时检查 DataContext、命令与业务绑定仍有效 |
| 异步播放与线程文化 | `PreviewLanguageUiTests`、`LocalizationUiTests`：选定语言后改变回调线程 UI 文化，验证播放 / 暂停状态与翻译仍使用服务快照；格式化仍按数值文化 |
| 首次显示、偏好、缺失语言包、选项稳定 | `SettingsStartupUiTests`、`SettingsWindowUiTests`、`ShortcutSettingsRecordingUiTests` 与相关设置单元测试 |
| 菜单、Dock、窗口标题与关闭 | `LocalizationUiTests`、`NativeMenuLifecycleUiTests`、`WorkbenchLayoutsUiTests`：直接 `SetLanguage` 即刷新；关闭后订阅释放 |
| 草稿及业务状态 | 相关 Draft 测试及 `LocalizationUiTests`：保存无效原始输入、选中 ID、工程快照和 Undo 状态；切换不触发编辑或保存偏好 |
| 发布规则 | `Tests/Build/AegiNext.Publish.Tests.ps1`、`scripts/publish/verify-package.ps1`：目录、内置语言元信息、无 BOM UTF-8、重复项与文件哈希 |

普通修改可选择以下命令中的相关组；命令示例不要求每次全部执行。首次运行缺少还原产物时先正常 restore，再使用 `--no-restore`。仅在确认相同配置下当前源码已编译时使用 `--no-build`。

```sh
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~LocalizationCatalogTests|FullyQualifiedName~WorkbenchLocalizationTests|FullyQualifiedName~SettingsLocalizationTests'
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj --no-restore --filter 'FullyQualifiedName~LocalizationUiTests|FullyQualifiedName~PreviewLanguageUiTests|FullyQualifiedName~SettingsStartupUiTests'
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --no-restore
git diff --check
git diff --cached --check
```

UI 测试变化还要编译 UI 测试项目；应用编译本身不能证明测试 fixture 的 AXAML 编译成功。若 Rider MCP 可用，对受影响 C# / AXAML 文件执行分析，并明确报告采用的 severity 与真实发现。

## 资源完整性与旧入口

- 比较内置 JSON 的原始 `Strings` key 集合，检查消费 key 真实存在；对格式化文案验证位置参数兼容。`Get` 会返回英文或 key，不能代替此检查。
- 不允许把旧 `Tag` 提取测试直接留成空集合。新覆盖以实际 `Loc`、程序化绑定和业务映射为对象，并断言发现集合非空。动态拼接 key 需检查对应映射或有限取值。
- 修改已有迁移文本哈希或计数断言前确认是本次授权的新文案；不得为消除失败盲目改期望值，也不得永久禁止合法新增 key。
- 使用现有加载器测试验证重复项。普通 JSON 解析后字典可能已覆盖重复字段，单纯读取最终字典不足以证明资源有效。
- 搜索旧类型与命名空间：`ControlLocalization`、`SettingsViewLocalization`、`AegiNext.Desktop.Localization`、`WorkbenchText`、`SettingsText`、`PreviewText`、`LayoutText`、`LogText`、`WorkflowLogText`、`WindowChromeProbeText`。检查 `Tag` 的真实用途，业务 Tag 不属于翻译入口。`WorkbenchTextFormatting` 是共享排版工具，不是旧文案表。

## 静态状态与窗口清理

使用现有 `LocalizationTestStartup` / UI bootstrap 初始化，不为测试添加公共 reset API，也不在同一进程重新 `Initialize`。独立目录的加载测试使用 `TemporaryLocalizationDirectory` 和 `LocalizationCatalog`。触及静态语言、文化或环境变量的测试串行执行，并在 `finally` / `Dispose` 恢复语言选择、当前与默认 UI 文化、数值文化及环境变量中实际修改的项；先恢复语言，再恢复被服务同步的文化。

使用 `UiTestEnvironment` 和 `MainWindowTestContext` 管理隔离偏好、会话及窗口；独立窗口在 `finally` 关闭。为订阅测试实际打开、切换、关闭对象，再检查关闭后的绑定不更新。需要 GC 证据时，将构造与关闭放在独立 helper，排除本地强引用，并处理 Avalonia 对最后焦点窗口的缓存：关闭完整 owner 树，必要时用普通窗口对照确认框架缓存。不得以框架缓存为理由删除释放断言或清空服务全部事件来让测试通过。

## 构建与发布变化

仅修改翻译文本时无需重建整个 native 工具链。触及复制、加载或发布规则时：

1. 检查桌面项目语言项仍包含 `TargetPath=i18n/%(Filename)%(Extension)`、`CopyToOutputDirectory` 与 `CopyToPublishDirectory`。
2. 检查实际 build、测试与 publish 输出的语言目录，核对内置文件元信息与源文件内容 / 哈希。路径依赖 `AppContext.BaseDirectory`，不能依赖当前工作目录。
3. 运行受影响发布 Pester 测试；修改 PowerShell 时同时执行可用的 PSScriptAnalyzer。运行已有包校验器：

   ```sh
   pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory "/absolute/path/to/package"
   ```

4. 生成新包后检查 JSON 已进入现有签名 / 文件哈希清单。直接编辑已发布包会使清单失效，需通过正常发布流程更新。
5. 用独立工作目录与隔离 `AEGINEXT_PREFERENCES_DIRECTORY` 启动自己的测试进程，记录资源加载及启动结果，结束后关闭该进程；保留用户原有应用进程与偏好。

## 结果边界

以实际测试结果判断，通过 / 失败 / 跳过分别记录；无测试被选中不算通过。发现失败先确认与修改的关系，必要时在保留工作区的隔离 HEAD 中复现，不自动豁免历史同名失败。

Headless 测试可证明绑定、状态、布局度量与生命周期，原生启动可证明资源路径和基础启动。它们不能证明中文／英文按钮图标、长文本裁切、真实 macOS 菜单或其他平台的最终显示正确。复杂原生视觉验收由用户截图和操作确认，交付时单独列出未验收项。
