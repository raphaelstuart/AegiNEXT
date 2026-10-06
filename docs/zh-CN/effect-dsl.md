# 字幕特效 DSL v1

[English](../effect-dsl.md) | [简体中文](effect-dsl.md)

本页对应真实解析器 `src/AegiNext.Core/Effects/EffectScriptParser.cs` 和时间编译器 `EffectScriptCompiler.cs`。内置文件会嵌入 Core 程序集，预设应用直接读取这些文件，样板与实际执行同源。

实现包括脚本解析、精确时间分配、向量与完整颜色动画、项目事务、七个内置预设、设置模板库和 `.aegifx` 导入导出。特效面板的预设列表来自内置与个人脚本库；应用时按目标 Clip 编译，不复用其他时长的关键帧快照。

## 编辑器与个人模板

打开“设置 → 特效脚本”，选择内置样板并另存为个人模板，或新增空白模板。内置脚本只读，另存时产生新的稳定脚本标识；个人模板名称不随界面语言变化。名称和源代码草稿在切换列表或语言时保留，保存会先验证整份个人库，再原子写入个人应用数据目录的 `effect-scripts.json`，与项目文件及布局文件独立。

编辑器使用原生 TextBox/TextPresenter 的同一文字布局，为指令、属性、函数、数值、字符串、插值和注释着色。真实字符输入自动显示当前指令上下文的补全，也可按 `Ctrl/Cmd + Space` 显式打开。补全浮层锚定原生光标位置，不占代码区域底部布局，不抢输入焦点；方向键选择，Enter/Tab 插入，Esc 关闭，也支持点击候选项。输入法预编辑期间关闭浮层，输入、选择、光标保留原生行为；失焦或切到只读模板时关闭。编辑器底部不再显示常驻操作提示。点击“验证”得到实际行列诊断，“定位错误”明确移动光标；验证失败不会抢焦点或覆盖源代码。

单个模板导入／导出为 UTF-8 `.aegifx`，导入时检查语法、预算及与个人／内置稳定标识的冲突。导入失败保持原库；导出也支持内置样板。内置标识保留，重新导入内置源之前应在设置中另存为，或将源的 effect 标识改为个人唯一标识。设置只负责管理脚本；保存后在工作台特效面板选择预设并点击“应用预设”，按当前字幕 Clip 时长生成动画。

调用 `$aeginext-effect-dsl` 可使用项目专用的 [DSL 书写 Skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md)。

## 样板位置

- [淡入淡出](../../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx)：固定入口、自由保持、固定出口，适合作为第一份样板。
- [填充与描边变色](../examples/effects/color-cycle.aegifx)：完整线性 RGBA、固定变色与自由保持。
- [滑入＋弹入＋淡出](../examples/effects/slide-pop.aegifx)：完整向量和同段多个属性、多个关键帧的自定义样板。
- 单独的 [淡入](../../src/AegiNext.Core/Effects/Scripts/fade-in.aegifx)、[淡出](../../src/AegiNext.Core/Effects/Scripts/fade-out.aegifx)、[弹入](../../src/AegiNext.Core/Effects/Scripts/pop-in.aegifx)、[弹出](../../src/AegiNext.Core/Effects/Scripts/pop-out.aegifx)、[滑入](../../src/AegiNext.Core/Effects/Scripts/slide-in.aegifx)、[滑出](../../src/AegiNext.Core/Effects/Scripts/slide-out.aegifx)。

所有文件均为 UTF-8 `.aegifx` 文本，可以直接编辑，`#` 后为注释。

```text
effect "fade-in-out" version 1
short-clip compress

segment enter fixed 300ms
    at 0 opacity 0 ease-out
    at 1 opacity base
end

segment stay flex 1
    at 0 opacity base hold
    at 1 opacity base
end

segment exit fixed 300ms
    at 0 opacity base ease-in
    at 1 opacity 0
end
```

## 时间与 Clip 时长

`fixed 300ms` 表示正常情况下保留 300 毫秒。时间可以使用 `s` 或 `ms`，支持最多六位小数，内部保留有理数精度，不量化到视频帧或毫秒。

`flex 1` 表示按权重分配剩余时间。多个自由段按各自权重分配，例如 1 和 3 分别得到剩余时间的 25% 和 75%。脚本必须至少有一个自由段，可以为空段；空段保持前面的属性值，直到下一段。

`at 0`、`at 1` 是当前段的起点、终点；`at 0.7` 是该段时长的 70%，也支持最多六位小数。同段同属性必须从 0 开始、以 1 结束，位置严格递增。不同属性可以交错书写。

以上淡入淡出脚本的分配示例：

| Clip 时长 | 入口 | 保持 | 出口 |
|---|---:|---:|---:|
| 5 秒 | 300ms | 4.4 秒 | 300ms |
| 1 秒 | 300ms | 400ms | 300ms |
| 600ms | 300ms | 0ms | 300ms |
| 400ms，compress | 200ms | 0ms | 200ms |
| 400ms，reject | 拒绝应用 | 原项目保留 | 原 Undo 保留 |

短片段行为由每个脚本显式声明，不隐含为全局规则：

- `short-clip compress`：Clip 短于固定段总和时，固定段按比例压缩，自由段为零。内置脚本使用此规则。
- `short-clip reject`：时长不足时拒绝应用，并显示时长诊断。

零时长自由段的所有点会落在同一时刻。其值必须相同；如果自由段本身有变化动画，就会给出端点冲突诊断，避免静默丢失动画。两个相邻段的同属性共享端点也必须有相同值，合并点使用右段的插值方式。未声明属性的区间保持已提交的上一值；不能通过省略一个段隐含制造属性跳变。

## 值与插值

| 属性 | 类型与单位 | 示例 |
|---|---|---|
| `position` | 二维局部像素平移 | `offset(-250, 0)`、`(100, 20)` |
| `scale` | 二维缩放 | `factor(0.2, 0.2)`、`(1, 1)` |
| `rotation` | 角度 | `offset(15)`、`0` |
| `opacity` | 0 到 1 | `0`、`base` |
| `fill` | 完整直通线性 RGBA 填充颜色 | `rgba(1, 0.1, 0, 0.8)`、`base` |
| `stroke` | 完整直通线性 RGBA 描边颜色 | `rgba(0, 0, 0, 1)`、`base` |
| `blur` | 0 到 512 | `0`、`base` |
| `stroke-width` | 0 到 4096 | `factor(2)`、`base` |
| `path-progress` | 0 到 1，显式字面值 | `0`、`1` |

`base` 表示应用脚本前的基础值；不读取播放头的动画求值，也不累计前一段的修改。`offset(...)` 为基础值加偏移，`factor(...)` 为基础值乘倍率。二维值分别作用于两个分量，因此非等比缩放可保留。字幕描边的基础值读取该字幕的样式。

`fill` 和 `stroke` 使用一个完整颜色变量。`rgba(...)` 的 RGB 是线性值，允许 HDR；Alpha 属于 0–1。只接受 `rgba(...)` 或 `base`，不接受颜色 `offset/factor`。`base` 读取目标字幕的样式基础色。字段中的 HEX 使用 sRGB，与脚本的线性数值不同；脚本继续以 `#` 表示注释，不直接接受 HEX 字面量。

颜色在项目中保存为完整 `{ "red": ..., "green": ..., "blue": ..., "alpha": ... }`。旧 v3 的分通道轨道按时间并集合并，缺少通道使用字幕样式基础色，各通道独立缓动与裁剪相位继续保留。主程序和独立压制 worker 共用 Core 序列化与求值。

`position` 不包含父组、路径或自然文字布局，只表示局部平移。它不改变自然文字边界、Anchor、Pivot 或字号。位置和缩放在项目 v3 中各保存一条向量轨道，关键帧 `value` 为 `{ "x": ..., "y": ... }`。属性面板和时间线使用同一完整向量。加载旧 v3 分量轨道时按两轴时间并集合并，并保留每轴独立缓动；新写入不保存分量轨道。

```text
segment enter fixed 300ms
    at 0 position offset(-250, 0) ease-out
    at 0 scale factor(0.2, 0.2) ease-out
    at 0.7 scale factor(1.08, 1.08) ease-in-out
    at 1 position base
    at 1 scale base
end
```

插值可选 `hold`、`linear`、`ease-in`、`ease-out`、`ease-in-out`，缺省为 `linear`。当前点的插值控制到该属性下一个点的区间。

## 校验与项目边界

- 标识和段名以英文小写字母开头，可包含小写字母、数字、`.`、`-`，最长 64 字符；段名不能重复。
- 最多 128 段、4096 个源关键帧、262144 个源字符。固定段最多 24 小时；自由权重为大于零、不超过 1000 的数。
- 仅接受本页的指令和值，不支持文件访问、网络、C#、循环或任意函数执行。
- 未知版本、属性、插值、维度错误、非有限数或越界值明确报错。文本诊断携带行列；编译还验证共享端点和目标基础值。
- 编译沿用片段的合法内容时钟，支持已裁剪片段的 `AnimationOffset`，所有点都在片段范围内。正常播放仍使用半开时间区间，结束关键帧不延长 Clip。
- 应用通过 `ProjectEditor.ApplyEffectScript` 完成一次原子事务；保留字幕身份、轨道归属、路径及未涉及的动画属性。失败不改项目、不增加 Undo；成功后关键帧可正常编辑、保存、重载、移动和裁剪。
