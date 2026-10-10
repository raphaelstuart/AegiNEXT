# 独立 libass 渲染对照

这些测试实际调用外部 libass 的解析器、字体整形与渲染器，再与 `AssSubtitleFormat.Parse` 导入后的 `ProjectSceneRenderer` 比较。绑定遵循 [libass 0.17.2 官方 ass.h](https://github.com/libass/libass/blob/0.17.2/libass/ass.h)。不经过项目 ASS 解析器生成外部基线；外部字体提供器关闭，双方固定使用仓库的 `Fixtures/NotoSans.ttf`。

运行时配置：

- `AEGINEXT_LIBASS_REFERENCE_PATH`：外部 libass 动态库的绝对路径；要求版本至少 0.17.2。
- `AEGINEXT_LIBASS_REFERENCE_PRELOAD`：可选依赖动态库绝对路径列表，使用当前平台 `Path.PathSeparator` 分隔，按依赖先后排列；Unix 使用全局加载，测试释放时逆序卸载。

未配置库时明确跳过；配置了不存在或不可加载的库时失败。库不下载、不随项目发布，也没有应用安装目录的源码硬编码。

```sh
dotnet test Tests/AegiNext.Rendering.Tests/AegiNext.Rendering.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~AssLibassReferenceTests'
```

## 指标与实际限制

计时、颜色比例和扫色进度测试保持严格阈值，覆盖整组 `k`/`ko` 激活、`kf` 的整组 advance、`ffi` 共享字形、空档、`kt` 重叠/倒序及 `r` 重置。单独的空 `1c`/`2c` 重置测试在原始 ASS 中显式指定透明阴影，严格验证 RGB 回到样式基准而两种视觉状态的 alpha 分别保留为 `127/255` / `63/255`；该测试不修改已知阴影损失样例，也不宣称覆盖 `3c`/`4c` 的外观。外部导出帧也与原始 ASS 基线核对。Skia 与 FreeType 的栅格化不同，因此这些指标通过不能解释为逐像素等价。

`CharacterizesReported...` 测试有意保留原始输入，断言差异超过原等价阈值，并要求导入、导出都出现 `Ass.ShadowComposition` 损失诊断。它们验证有损结论，不能计为渲染等价通过：

- `Shadow=0`、不透明背景色、零偏移和零 blur：libass 不产生阴影层；本地渲染仍产生阴影，样例激活/未激活填充 alpha 为 `127/255` / `63/255`，外部最大 alpha 分别约 0.498 / 0.247，本地为 1。alpha 等价容忍值为 0.002。
- 有描边且有偏移阴影：libass 的阴影包含描边，本地阴影只使用填充墨迹。固定样例 `ScaledBorderAndShadow=no/yes` 的右侧墨迹扩张差约 7 / 13 px；负时刻瞬变描边/阴影样例的增量差约 8 px。几何等价容忍值仍为 3 px。
- 即使描边颜色完全透明，libass 阴影仍包含其轮廓；阴影颜色完全透明的负对照不应出现该诊断。

原始 libass 帧还确认 `t(-500,-500,...)` 是瞬变，与直接样式完全相同；`t(0,0,...)` 按整段事件时长插值，不能表达零时刻瞬变。

当前 16 个用例应分别解读为：1 个运行时/字体检查、7 个计时、颜色和 alpha 指标对照、5 个明确报告的外观差异特征测试、3 个外部语义和诊断负对照。没有声称 16 个用例全部证明无损。
