# 渲染集成

[English](../en/rendering.md) · [简体中文](rendering.md) · [全部指南](README.md)

## 使用场景渲染器

`src/AegiNext.Rendering` 提供独立于 UI 的排版与场景渲染。求值不可变工程并准备资源后，通过 `ProjectSceneRenderer` 生成预览/导出画面。各线程拥有自己的渲染器和可释放表面，Core 不增加图形依赖。

场景支持字幕、保留的形状/图片/组、关键帧、三次路径、卡拉 OK、Clip 蒙版、模糊和扩展混合。工作台编辑字幕/蒙版，尚无通用形状/图片/组工具。

## 像素契约

| 字段 | 含义 |
|---|---|
| 存储 | 紧密、从上到下的 RGBA Half/F16 |
| 色彩 | 扩展线性 BT.709/sRGB 原色，允许有限负值和大于 1 的 RGB |
| Alpha | 预乘、0–1；零 alpha 要求零 RGB |
| 参考白 | 显式 `ReferenceWhiteNits`，默认 203；通道 1 对应参考白 |
| 坐标 | 左上原点，X 向右/Y 向下，目标像素 |
| 合成 | 线性 source-over，表面参考白必须相同 |

像素复制到调用方自有存储，不暴露借用原生内存。预览只在最终合成后转换成 SDR BGRA8；HDR 导出使用原始帧与 F16 叠层，显示像素不参与编码。

## 文字与编辑几何

`TextShaper` 使用显式字体字节、字号、方向和语言。字形 cluster 是 UTF-16 索引而非字符数；run API 要求有效单行/单文字系统文本，调用方负责分段，混合双向/逐 run 字体回退仍不完整。

场景渲染增加基础多行/换行和字素卡拉 OK。实际字形墨迹边界决定几何，描边/阴影/模糊不改变 Pivot；空文本使用逻辑编辑框。Anchor 相对画布、Pivot 相对墨迹、Offset 使用像素。`GetLayerGeometry` 与绘制、命中和拖拽共享边界/变换。

## Clip 蒙版

先渲染完整字幕和自身模糊，再在工程坐标裁切，随后合成到父层。蒙版仅影响所属 Clip；字幕/父层变换不移动蒙版，蒙版自身固定 Pivot 变换负责移动。保留轮廓方向、非零环绕规则和反相；预览缓存身份包含求值后的蒙版几何。

## 可选 macOS HDR 诊断

构建 `-Target All`，再以 `--hdr-probe` 启动匹配桌面产物。独立诊断通过 Media → Vulkan/MoltenVK/libplacebo → FP16 Linear Display P3/CAMetalLayer EDR 呈现 F16；与 SDR 工作台和视频导出独立，Windows HDR 显示暂未实现。

上传采用显式参考白归一化和当前显示器 headroom。标称 203 nits 是应用尺度，不是实测屏幕亮度。创建/呈现/销毁遵守主线程所有权，关闭等待原生销毁；依赖锁定见 `native/dependencies.json`。

## 验证

```powershell
dotnet test Tests/AegiNext.Rendering.Tests/AegiNext.Rendering.Tests.csproj -c Release
```

测试加载真实 Skia/HarfBuzz 和固定[字体素材](../../Tests/AegiNext.Rendering.Tests/Fixtures/README.zh-CN.md)，检查像素、alpha、几何和扩展值。离屏回读和标签不能证明物理 HDR 外观，显示验收需要目标屏幕。
