# 离屏渲染契约

[English](../rendering.md) | [简体中文](rendering.md)

Step 1.2 提供 `AegiNext.Rendering`：独立于 UI 与项目模型的 Skia/HarfBuzz 适配层。当前实现使用 CPU raster 表面，验证文字和图形层的高精度数值链路；不包含 GPU 互操作、显示器映射、视频解码、时间求值或编码。

## 颜色、像素与坐标

| 项目 | 固定契约 |
| --- | --- |
| 工作空间 | 扩展线性 sRGB，允许负 RGB 和超过 1 的 RGB。 |
| 亮度 | `RenderSurfaceInfo.ReferenceWhiteNits` 必须显式指定；中性 `(1,1,1)` 代表该亮度的参考白。测试使用 203 nits，API 不强制该数值。 |
| 绘制颜色 | `LinearColor` 是未预乘线性 RGBA；RGB 有限且位于 `[-65504,65504]`，alpha 位于 `[0,1]`。默认值为透明黑。 |
| 编码颜色输入 | `LinearColor.FromSrgb` 仅接受 `[0,1]` 的 sRGB 编码 RGB，执行传递函数解码，alpha 原样保留。 |
| 存储 | 非归一化 `RgbaF16`、预乘 alpha；原始 HDR RGB 可以大于 alpha。不是 `RgbaF16Clamped`，也不经过 `SKColor` 的 8-bit 通道。 |
| 坐标 | 左上原点、x 向右、y 向下，单位为目标像素；文字坐标为基线原点。所有坐标及几何终点必须有限。 |
| 合成 | source-over 在线性空间执行，图层 opacity 作用于源表面整体。两个表面的参考白必须完全一致，否则拒绝合成。 |
| 输出 | `CopyPixels(Span<Half>)` 复制到调用方拥有的缓冲：从上到下、每行紧密排列、逐像素 RGBA。字节数量和行跨度在创建描述时 checked 检查。 |

Skia paint 接收未预乘浮点颜色，并显式传入线性色彩空间；不能改成无颜色空间的 `ColorF` setter。`SKSurface` 创建时显式固定紧密行跨度。每次读取均复制原始 Half 数值，不解预乘、不做 tone mapping，也不借出原生像素内存。[SkiaSharp SKPaint](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKPaint.cs)、[SKPixmap](https://github.com/mono/SkiaSharp/blob/v3.119.4/binding/SkiaSharp/SKPixmap.cs)

F16 有限精度可能产生舍入和很小数值的下溢。当前测试对普通样本使用明确的绝对误差；极值样本单独验证。原生显示与编码模块后续必须解释这些像素的原色、传递函数、参考白和 alpha 语义，不能仅依据 Half 字节猜测色彩含义。

## 字体塑形边界

`TextShaper` 从调用方提供的字体字节和 face index 创建资源。字体数据被复制，不查询系统字体，不静默回退；缺字、损坏字体、非法文本、不可表示的字号结果均明确抛出异常。

`Shape` 接收非空单行、单脚本文本、像素字号、显式 LTR／RTL 方向与语言，脚本由 HarfBuzz 根据该 run 推断。输入为 UTF-16，`ShapedGlyph.Utf16Cluster` 是源字符串的 UTF-16 code unit 下标。一个 cluster 可以包含多个字符和多个 glyph；代理对占两个 code unit。`GlyphId` 是字体字形索引，不是 Unicode code point。控制字符、换行及不配对的代理项在进入原生塑形前被拒绝。

该 API 是文本 run 的塑形与绘制基础。混合双向段落排序、脚本分段、换行、字体 fallback、富文本、竖排及逐字特效布局仍须由后续模块完成。调用方应先完成 run 分段，不能把整段混合双向文字作为一个 run 并假设自动完成布局。[HarfBuzz 能力边界](https://harfbuzz.github.io/what-harfbuzz-doesnt-do.html)、[SKShaper UTF-16 buffer 入口](https://github.com/mono/SkiaSharp/blob/v3.119.4/source/SkiaSharp.HarfBuzz/SkiaSharp.HarfBuzz/SKShaper.cs)

`ProjectSceneRenderer` 在上述基础 API 上实现项目场景：换行／基本换行布局、段落级字体回退、Unicode 字素卡拉 OK、关键帧、三次 Bézier 路径、字幕／形状／图片、嵌套组与动画 Clip 蒙版、扩展线性混合和 F16 模糊。工作台提供 Clip 蒙版编辑，一般形状／图片／组工具仍在字幕编辑器范围之外。普通文字塑形仍按单方向段落处理，未完成混合双向排序和逐 run 字体回退。预览和独立 worker 共享这一层，项目先验证并准备求值索引，资源在渲染器生命周期内缓存；多线程入口分别拥有渲染器。

`SceneEvaluator` 按字幕内容时钟求值矩形边界、蒙版整体变换及稳定 ID 的节点／控制柄轨道，输出 `EvaluatedLayer.Mask`。`ClipMask` 仅作用于所属字幕 Clip。渲染先在 F16 表面完整绘制文字、描边、阴影与卡拉 OK，并应用 Clip 自身模糊，再以工程画面坐标裁切，最后合成到父层。字幕及父层变换不改变蒙版，蒙版自身的固定轴心变换独立生效。自由路径保留轮廓方向并采用 nonzero winding 填充，反相保留工程画面内填充区域之外的部分。父组效果沿用既有顺序，裁切不影响兄弟图层；预览缩放将同一几何投影到渲染表面，视频压制复用同一路径。预览等价判断比较求值后的几何，同一时间编辑及仅蒙版动画都会使旧缓存失效。

`ShapedTextRun.InkBounds` 根据实际 glyph bounds 与塑形位置计算有墨迹范围，不使用字形 advance 或保守的 blob 整体范围定位。`ProjectSceneRenderer.GetLayerGeometry` 返回和实际绘制一致的局部边界、Pivot、基准位置及变换矩阵；字幕位置采用画布归一化 Anchor、字形归一化 Pivot 和像素 Offset，旋转／缩放围绕实际文字轴心执行。描边、阴影和模糊不改变字形 Pivot；纯空格提供标记为 `HasInk=false` 的逻辑编辑范围。

指定输出尺寸的 `ComposePreview` 重载将视频等比例置入目标表面并保留黑色留边，项目叠层同样等比例变换。桌面预览目标保持项目宽高比，特效画布使用同次转换的未合成视频背景，避免字幕二次叠加。原有重载保留调用方指定背景尺寸的行为；HDR 导出继续直接使用项目 F16 表面。

组、蒙版和模糊明确使用 F16 中间表面。相加等合成采用扩展线性计算，保留大于 1 的亮度和预乘 alpha，避免普通 Skia 中间图像导致的亮度裁剪；导出调用同时保留项目参考白。预览的最终 sRGB/BGRA8 显示派生不会成为 HDR 导出源。

## 资源与调用示例

`LinearRenderSurface` 与 `TextShaper` 均限单线程使用，调用方负责 Dispose。`ShapedTextRun` 持有自身绘制资源，允许在 shaper 释放后继续绘制；Dispose 后仅保留可读取的托管布局元数据，原生绘制会抛出 `ObjectDisposedException`。表面合成同步读取源快照；合成完成后释放或清除源表面不会改变目标。

```csharp
using var shaper = new TextShaper(fontBytes);
using var text = shaper.Shape("AegiNext ffi", 48, TextDirection.LEFT_TO_RIGHT, "en");
using var layer = new LinearRenderSurface(new(1920, 1080, 203));
layer.DrawText(text, new(64, 980), new(2, 2, 2, 1));

var pixels = new Half[layer.Info.ChannelCount];
layer.CopyPixels(pixels);
```

上述示例使用线性亮度为参考白两倍的文字。它只生成离屏像素，不能据此宣称屏幕已显示 HDR。

## 可复现验证与依赖

渲染测试位于 `Tests/AegiNext.Rendering.Tests`，实际加载原生库。测试字体固定在 [Fixtures](../../Tests/AegiNext.Rendering.Tests/Fixtures/README.zh-CN.md)，包含哈希、官方来源和 OFL 授权；不依赖用户安装的字体。

SkiaSharp 与桥接包固定为 `3.119.4`，HarfBuzzSharp 托管及所有参与依赖解析的原生平台包固定为 `8.3.1.5`。通过集中传递依赖锁定同步更新 Desktop 的 HarfBuzz 依赖，避免后续同一进程出现不同版本；Core 不增加任何图形依赖。平台原生窗口和离屏渲染测试的验收结果分别记录在 [Checkpoint](README.md#实施与验收记录)。
