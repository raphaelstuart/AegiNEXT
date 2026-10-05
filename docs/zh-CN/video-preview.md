# SDR 视频预览

[English](../video-preview.md) | [简体中文](video-preview.md)

## 模块与数据边界

`native/decoder` 在既有 ABI 1 上增加能力位 `SDR_PREVIEW = 2`、转换器所有权及 BGRA8 交付接口。旧 decoder/frame 结构不变。托管适配器位于 `AegiNext.Media/Preview`，桌面控制器位于 `AegiNext.Desktop/Controllers`。Core 不依赖显示像素或 FFmpeg。

`SdrVideoConverter` 同步借用 `DecodedVideoFrame` 的独立引用，在后台串行转换，不取得源帧所有权。输出 `SdrVideoFrame` 是独立、不透明、top-down、tight BGRA8 的 sRGB 显示图像，正方形像素。转换、后续 seek、decoder 关闭及修改另一份输出不会改写原始 planes、PTS 或 HDR 事实。

预览不能作为 HDR 合成／压制输入。后续导出继续直接读取原始高精度帧；本步没有实现编码，也不能仅凭原始 HDR 信息仍存在宣称导出保真。

## 明确的色彩政策

固定 FFmpeg `9.0.2`，新增 `libswscale 10.1.102`，与解码共享显式 SDK 根目录、版本 manifest 及运行版本检查。使用 `sws_alloc_context` 的 dynamic frame API，限制稳定 CPU backend，显式设置 `SWS_INTENT_PERCEPTUAL`、strict 和固定舍入／抖动策略。CPU worker 数按实际逻辑核心数限制为 1 至 4；转换器本身仍由一个消费者串行调用。不能以 legacy `sws_init_context` 初始化此 CMS context；其默认 relative-colorimetric intent 也不能替代感知映射，因为高光会裁掉。[固定版本 API](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/swscale.h)、[CMS 实现](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/cms.c)

不启用可选的 `SWS_FULL_CHR_H_INT`。实际 BT.709 YUV420 素材曾在该版本的 CMS 中间 RGBA64 路径出现逐行色度污染；最小恒定非中性色度样本也能复现，关闭该标志后消失。固定版本 full-chroma 输出路径的负色度插值与普通路径存在有符号处理差异，现有证据支持其为原因；不在本项目修改 FFmpeg 依赖源码。[固定版本输出实现](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/output.c)。回归检查恒定色块各行色相、奇数裁剪及 1／2／3／4 worker 的 SDR／sRGB／PQ／HLG 输出性质。

目标 frame 为 RGB matrix、full range、BT.709 primaries、sRGB transfer，不继承源 HDR side data。固定版本无目标 mastering 时使用 203 nit 的 SDR 参考亮度；这是软件映射标度，不是实际显示器亮度，也没有进行 ICC 显示器校准。[色彩事实读取](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/format.c)

- PQ 使用绝对亮度 EOTF。无 mastering 时使用固定版本的默认 10000 nit 源峰值；有效静态 mastering 会影响映射峰值／内容色域，不重新定义 PQ 码值。非法 mastering 明确拒绝。
- HLG 的转换视图移除 mastering，固定为 1000 nit、黑位 0 的参考显示条件，对应 system gamma 1.2，含 OOTF。原始 frame 的 mastering 仍保留。
- MaxCLL／MaxFALL 原样保留，但固定版本 CMS 不读取它们来决定曝光，不把它们填成 mastering peak。
- BT.709 成片的显示解释采用锁定 FFmpeg CMS 的 EOTF；sRGB 使用独立传递函数，二者没有混用。

这些政策来自固定版本的[色彩函数](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libavutil/csp.c)与元数据读取实现。不同平台使用容差／性质断言，不承诺 SIMD 浮点结果逐字节一致。

## 输入与几何

首步接收已支持的三分量、不透明、逐行整数 RGB／YUV，分量深度 8 至 16 bit。要求明确的 BT.709／BT.2020 primaries、BT.709／sRGB／PQ／HLG transfer、range 与相应 matrix；子采样输入还必须有已知 chroma location。托管层筛选常见格式，原生层再依据实际像素 descriptor 和后端能力检查。YUVJ 与 limited range 的冲突明确拒绝。

未知色彩不按分辨率猜测，不把流级事实写进帧快照；本步没有人工覆盖工具。带 alpha、隔行、浮点像素、display matrix／旋转、立体画面、动态 HDR／Dolby Vision、ICC／raw color、film grain 与 ambient viewing metadata 尚不支持，明确提示。

先对 coded 尺寸完成 CMS，再从 BGRA8 精确取可见 crop，最后以独立 BGRA bilinear resampler 适配预览边界。这样 odd crop 不受 YUV 色度对齐取整影响。输出比例使用 `(codedWidth - left - right) × SAR / visibleHeight`；没有 SAR 时采用明确的 1:1 预览政策，不补写原始信息。Media 基础转换默认最大 1280 × 720；桌面选择默认低 960×540、标准 1280×720 或高 1920×1080，不放大小视频；交互最高 540p，松手恢复所选级别。输出像素预算 16,777,216，coded 像素预算 33,177,600。转换过程中仍会分配完整 coded 中间图像，尚未验收 4K／8K 实时性能。

## 桌面生命周期

桌面控制器持有尚未打开完成的 session，关闭／换文件调用其异步关闭；不能仅取消 `OpenAsync` 的命令 token。每份显示结果关联文件 epoch、session identity、播放 generation 与控制器 revision，在转换结束和实际 UI 交付时验证，旧文件／旧定位画面不得回写。

帧消费者单个运行，UI 交付有界并等待 Dispatcher。读取到 EOF 后等待控制恢复，后续 Seek 可再次显示。关闭先使身份失效，再终止读取／会话并等待转换结束，最后释放显示位图；已经读出的原始帧由消费者负责释放。CPU 转换是同步工作，取消在调用前后检查，不承诺在单次 CMS 内立即抢占。

播放时钟独立、单调推进。尚未转换的队列可以淘汰过期帧；已经完成转换的最新画面只要身份有效、同代 PTS 不倒退就交付，不再因为 UI 交付时已跨过其原始结束时刻而丢弃。否则转换耗时接近一个帧间隔时会形成连续丢帧。EOF 仍只接受会话选定的尾帧。控制器快照分别记录源 `PresentedFrameTime`、交付时 `PresentedAtPosition` 和 `PresentedGeneration`，便于测量延迟；Open／Seek／Pause／Close 立即清空旧测量，回调成功且再次验证身份后才提交新测量。这些字段不改变源时间或主时钟。

本页原始阶段只覆盖视频显示；当前工作台已经接入音频、语谱图、字幕编辑和独立 HDR 压制，见 [工作台手册](workbench.md)。真实屏幕观感和交互流畅度由用户视觉验收，数值／生命周期测试不替代这些结论。
