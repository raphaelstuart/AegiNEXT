# macOS 原生 HDR 诊断

[English](../native-hdr.md) | [简体中文](native-hdr.md)

## 范围

Phase 1 / Step 1.3 验证静态 F16 字幕／图形帧通过 Avalonia 原生子视图呈现到 macOS EDR。入口为 `--hdr-probe`；当前默认应用入口为正式字幕工作台。诊断 UI 不是正式编辑界面，尚未实现产品主题、本地化、视频解码或播放时钟。

后续用户已确认优先普通 SDR 预览与保持 HDR 的压制导出。此入口保留为可选技术诊断，Windows HDR 预览暂缓，不作为正式编辑器或默认构建的前置条件。

```text
AegiNext.Rendering（线性 sRGB、预乘 RGBA F16）
  → AegiNext.Media（帧校验、SafeHandle、C ABI）
  → 原生 F32 归一化上传
  → libplacebo / Vulkan / MoltenVK
  → FP16 Linear Display P3 / CAMetalLayer EDR
```

Core 不引入图形或原生依赖。原生帧直接呈现到 NSView，不经过 Avalonia Bitmap 或 RGBA8。

## 开发依赖与构建

版本与许可信息记录于 `native/dependencies.json`。当前锁定 libplacebo 7.360.1、MoltenVK 1.4.2、Vulkan-Headers 1.4.357.0；libplacebo 必须具备 Vulkan、自定义函数入口和 shaderc 能力。CMake 在版本或能力不符时停止，不自动使用其他渲染路径。

本机安装了 CMake 4.4.3、Ninja 1.13.2、pkgconf 3.0.7、shaderc 2026.4。以下命令适用于上述版本仍可获得的开发环境；Homebrew 将来升级后不能视为可复现依赖来源，需要按清单提供对应版本。

```sh
brew install powershell
pwsh -NoProfile -File ./build.ps1 -Target All -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target All -InstallDependencies -RunTests -TestProjects Media
```

本机曾因 Homebrew 镜像下载失败，安装时以单次命令环境覆盖为官方源：`HOMEBREW_API_DOMAIN=https://formulae.brew.sh/api`、`HOMEBREW_BOTTLE_DOMAIN=https://ghcr.io/v2/homebrew/core`；没有修改持久配置。

原生输出按 RID 与配置隔离，例如 `artifacts/native/osx-arm64/Release/libaeginext_media.dylib`。Media 项目按相同 RID／配置复制已有文件；`-Target All` 保证先原生后托管。原生库缺失时普通主窗口仍可运行，HDR 诊断会明确显示加载失败。更多参数见 [构建说明](building.md)。

**当前是本机开发产物。** 主模块的部署目标为 macOS 14，但本机 libplacebo 为 macOS 27 构建，链接器对此有明确警告。此早期临时 bundle 未完成完整发布流程；后续开发包已有闭包、路径改写、原始许可和签名验收，见 [平台发布](publishing.md)。macOS 14 依赖重建与发行公证仍未验收。`artifacts/AegiNext.app` 是界面检查用的临时 bundle，不可作为发布包。

## 像素与亮度契约

- 输入为紧密排列的 little-endian RGBA Half；C ABI 另外支持有填充的行跨度。托管 `HdrFrame` 复制并独占输入，原生 Present 在返回前完成对调用方输入的使用，不保留托管指针。
- RGB 为扩展线性 sRGB 的预乘值，允许负值和大于 1 的高亮；alpha 必须有限且在 `[0,1]`。alpha 为 0 时 RGB 必须均为 0。
- `ReferenceWhiteNits` 显式给出源信号参考白。`SourcePeakNits` 是调用方给出的 RGB 信号峰值上界，必须覆盖所有正的非预乘 RGB，允许 0.1% 量化容差。该值不是对画面最大 Y 亮度或显示器物理亮度的测量。
- 上传时以 F32 计算 `RGB × ReferenceWhiteNits / 203`，alpha 保持原值，避免先用 Half 缩放导致中间溢出或量化损失。非有限值及非零下溢会拒绝。
- libplacebo 的 Linear 输入不会仅因设置亮度元数据自动改变像素标度，因此必须执行上述显式归一化。目标峰值每帧设置为 `203 × currentHeadroom`。
- EDR 1 相对于当前屏幕 SDR 白。203 仅为应用／libplacebo 的名义标度；不能据此断言物理屏幕亮度为 203 nits。
- 每帧读取当前屏幕、backing scale 与 EDR headroom，保留源画面比例。不可见、最小化、零尺寸或暂时没有 drawable 时返回 NOT_READY，由宿主稍后重试。
- 逐帧验证实际交换链为 4×16-bit float、Linear Display P3，CAMetalLayer 为 RGBA16Float、扩展线性 P3、EDR 开启且没有额外 EDRMetadata 映射。格式不符明确失败。

## ABI 与生命周期

公开 C ABI 位于 `native/include/aeginext_hdr.h`，版本为 1。状态结构 72 字节，GPU 验证结构 32 字节；输入结构必须初始化 size 与 version。原生错误码与 UTF-8 错误缓冲跨边界传递，C++／Objective-C 异常不会越过 ABI。

NSView 由原生会话持有，Avalonia 借用并挂载。创建、呈现和 GPU 验证要求主线程；SafeHandle 的异常终结兜底会将销毁投递到主线程，正常路径由 NativeControlHost 显式销毁。解除可视树附着只停止计时器，真正销毁以 Avalonia 的 `DestroyNativeControlCore` 为准，避免重新挂载时借用已释放的视图。

诊断窗口关闭时取消自动检查，等待检查任务结束，然后移除宿主、等待原生销毁、记录存活数并写入报告，最后退出进程。取消令牌源由自动检查事件处理方法中的 `using` 局部变量持有，通过 `Closing` 事件取消，在检查结束后解除订阅并释放；窗口没有额外的同步 Dispose 入口。

## 运行与报告

交互验收：

```sh
dotnet run --project src/AegiNext.Desktop --configuration Release --no-build -- \
  --hdr-probe \
  --hdr-probe-font Tests/AegiNext.Rendering.Tests/Fixtures/NotoSans.ttf \
  --hdr-probe-report artifacts/verification/step-1.3-hdr-manual.json
```

自动集成验证：将 `--hdr-probe` 改为 `--hdr-probe-auto`。成功退出码为 0，失败为 1；完成后检查 JSON 的 `Failure`、GPU 验证结果及生命周期字段。自动流程等待真实呈现，调整窗口尺寸，检查同步重新附着不重建会话，再执行真正解除附着／重建，最后验证关闭后的存活数为 0。

测试图包含 0、0.18、0.5、1、2、4 倍参考白灰阶、2 倍 RGB、半透明高亮圆形；指定字体路径时还包含 2 倍高亮的实际塑形文字。

验证层次分别为：

1. Media 单元测试：帧输入、参考白／峰值边界、Half 极值、预乘 alpha、拷贝所有权及 ABI 布局，不调用 GPU。
2. 原生 CTest：一个测试程序，包含六组 CPU／ABI 契约测试，不创建 GPU 会话。
3. GPU 验证：实际 F16 上传／读回，负值及 Half 极值保留，sRGB→P3 原色转换，两种等价参考白输入比较，4 倍白保留。读回来自离屏目标，不是交换链屏幕截图。
4. 原生呈现：核验实际交换链和 layer 属性，验证宿主缩放、重挂与销毁。
5. 人工显示验收：真实屏幕亮度层次、透明边缘、跨屏和系统显示设置变化。截图不能证明物理 HDR 亮度。

当前执行结果见 [Checkpoint](README.md#实施与验收记录)。Windows、macOS 14、视频 PTS、实时性能、最小化恢复、复杂合成、编码输出和显示器色度计测量都不由本步测试证明。
