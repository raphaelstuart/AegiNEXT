# 独立 libass 渲染对照

这些测试实际调用外部 libass 的解析器、字体整形与渲染器。原生画面对照使用 `AssSubtitleFormat.Parse` 导入后的 `ProjectSceneRenderer`；源操作约束对照则使用手写静态 ASS 帧，同时检查原始输入与项目导入再导出的 ASS 在 libass 中的表现。绑定遵循 [libass 0.17.2 官方 ass.h](https://github.com/libass/libass/blob/0.17.2/libass/ass.h)。外部基线不经过项目 ASS 解析器；外部字体提供器关闭，双方固定使用仓库的 `Fixtures/NotoSans.ttf`。

运行时配置：

- `AEGINEXT_LIBASS_REFERENCE_PATH`：外部 libass 动态库的绝对路径；要求版本至少 0.17.2。
- `AEGINEXT_LIBASS_REFERENCE_PRELOAD`：可选依赖动态库绝对路径列表，使用当前平台 `Path.PathSeparator` 分隔，按依赖先后排列；Unix 使用全局加载，测试释放时逆序卸载。

未配置库时明确跳过；配置了不存在或不可加载的库时失败。库不下载、不随项目发布，也没有应用安装目录的源码硬编码。

```sh
dotnet test Tests/AegiNext.Rendering.Tests/AegiNext.Rendering.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~AssLibassReferenceTests|FullyQualifiedName~AssContinuousAnimationReferenceTests|FullyQualifiedName~AssShadowClampReferenceTests|FullyQualifiedName~AssKaraokeNormalFillReferenceTests|FullyQualifiedName~AssNumericClampReferenceTests'
```

## 指标与实际限制

计时、颜色比例和扫色进度测试保持严格阈值，覆盖整组 `k`/`ko` 激活、`kf` 的整组 advance、`ffi` 共享字形、空档、`kt` 重叠/倒序及 `r` 重置。单独的空 `1c`/`2c` 重置测试在原始 ASS 中显式指定透明阴影，严格验证 RGB 回到样式基准而两种视觉状态的 alpha 分别保留为 `127/255` / `63/255`；该测试不修改已知阴影损失样例，也不宣称覆盖 `3c`/`4c` 的外观。外部导出帧也与原始 ASS 基线核对。Skia 与 FreeType 的栅格化不同，因此这些指标通过不能解释为逐像素等价。

`CharacterizesReported...` 测试有意保留原始输入，断言差异超过原等价阈值，并要求导入、导出都出现 `Ass.ShadowComposition` 损失诊断。它们验证有损结论，不能计为渲染等价通过：

- `Shadow=0`、不透明背景色、零偏移和零 blur：libass 不产生阴影层；本地渲染仍产生阴影，样例激活/未激活填充 alpha 为 `127/255` / `63/255`，外部最大 alpha 分别约 0.498 / 0.247，本地为 1。alpha 等价容忍值为 0.002。
- 有描边且有偏移阴影：libass 的阴影包含描边，本地阴影只使用填充墨迹。固定样例 `ScaledBorderAndShadow=no/yes` 的右侧墨迹扩张差约 7 / 13 px；负时刻瞬变描边/阴影样例的增量差约 8 px。几何等价容忍值仍为 3 px。
- 即使描边颜色完全透明，libass 阴影仍包含其轮廓；阴影颜色完全透明的负对照不应出现该诊断。

原始 libass 帧还确认 `t(-500,-500,...)` 是瞬变，与直接样式完全相同；`t(0,0,...)` 按整段事件时长插值，不能表达零时刻瞬变。

用例分为 33 项：原有 16 项包括 1 个运行时/字体检查、7 个计时、颜色和 alpha 指标对照、5 个明确报告的外观差异特征测试、3 个外部语义和诊断负对照；连续动画包含 1 项 RGB／分通道 alpha 和 2 项绝对／相对字号增长对照；源约束和状态填充增加 14 项。RGB 按 sRGB 编码空间比较。

字号测试以各自零时刻墨迹尺寸归一后比较增长，容许 3 px 栅格差异。固定字体下 Skia 与 FreeType 的字号度量基线仍不同，因此这两项证明的是连续动画的相对增长，不能解释为绝对字号或逐像素等价。所有用例也没有证明每一种 ASS 组合都无损。

新增源约束测试使用独立手算值生成每个时刻的静态 ASS 基线：5 项阴影测试覆盖有符号起值、负 shad 目标、源顺序和瞬时边界；6 项数值测试覆盖非正字号复位、负缩放、描边与 blur 的逐操作上下界。原始输入和导入再导出的结果都必须与静态基线对应。非空墨迹能量容忍 2%，边界容忍 1 px；空帧严格要求为空。数值测试另要求最大 alpha 差不超过 0.005。阴影样例采用无描边和不同的填充／阴影颜色，隔离已知合成差异，不证明通用原生阴影等价。

3 项卡拉 OK 填充测试比较原生画面、手写 ASS 次要颜色轨道和实际出口：普通 FILL 继承、静态未激活覆盖、动画未激活覆盖分别测试，激活时刻保持绿色填充。颜色阈值为 `1/255 + 0.001`，最大 alpha 阈值为 0.002；没有放宽既有外观差异特征测试。
