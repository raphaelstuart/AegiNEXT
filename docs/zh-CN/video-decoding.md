# 顺序视频解码

[English](../video-decoding.md) | [简体中文](video-decoding.md)

Phase 1 / Step 1.6 建立独立的软件解码输入：`native/decoder` 生成 `aeginext_decode`，`src/AegiNext.Media/Decoding` 负责托管契约和 SafeHandle 所有权。该库只依赖锁定的 FFmpeg 开发包，与可选的 macOS HDR 诊断库分开构建，不要求 libplacebo、MoltenVK 或窗口。

Step 1.6 支持本地文件、显式视频流索引、顺序读取、完整 EOF drain、协作取消和独立帧保留；Step 1.7 增加关键帧 seek 与精确显示帧选择。以上为原始模块阶段边界；当前工作台已集成播放、音频、颜色转换和独立导出，见 [工作台手册](workbench.md)。

## 构建与平台

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Decoder -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target Decoder -RunTests
pwsh -NoProfile -File ./build.ps1 -Target Managed
```

`Decoder` 只构建原生解码库；随后构建 Managed，按 RID 和 Configuration 复制到托管输出。默认检查缺项并退出，只有 `-InstallDependencies` 才安装。macOS 使用 Homebrew，Windows x64 使用 Scoop 的 `main/mingw`、`main/cmake`、`main/ninja`、`main/ffmpeg-shared`。SDK 的选择、版本检查及 `-FfmpegRoot` 用法见 [构建说明](building.md)。普通 FFmpeg CLI 包不能替代开发头文件、导入库和 shared runtime。

macOS 产物为 `artifacts/native/osx-<arch>/<Configuration>/libaeginext_decode.dylib`；Windows 产物为 `artifacts/native/win-x64/<Configuration>/aeginext_decode.dll`，所选开发包的 DLL 一同暂存。`Native`／`All` 仍指向此前的 macOS HDR 诊断库，不包含 Decoder。

源码和构建入口包含 Windows 路径，但目前只在 Apple Silicon macOS 27 实际编译和运行。本机 FFmpeg 动态库自身要求 macOS 27；解码库设置 macOS 14 部署目标不能消除依赖限制。Windows、最低系统版本、完整依赖打包及许可证交付仍待后续验证。Linux Decoder 本步明确延期。

## 时间与帧事实

- `Open(filePath, videoStreamIndex)` 接收容器中的绝对流索引，不能把“第一个视频流”序号直接当作流索引；调用方可先用 FFprobe 探测器取得全部流。
- `ReadFrame()` 正常结束返回 `null`，错误和取消抛出异常。先接收已缓存帧，再送入 packet；demux EOF 后发送 drain，并接收直至 decoder EOF，保留最后的 B 帧。
- `VideoFrameInfo` 是不可变快照，分别保留原始 PTS、best-effort 时间戳、duration、有效时基、原始 `AVFrame.time_base` 与 stream 时基。PTS／duration 使用有效帧时基；best-effort 使用 stream 时基。未知字段为可空值，不以帧率、帧号或零补齐。
- 保留原生像素格式、各 component 的位深、帧尺寸、SAR、裁剪边界、关键帧／损坏／交错标志和原始 decode error flags。
- 帧级色彩记录同时保留 FFmpeg 数字枚举和名称：范围、矩阵、原色、传递函数、色度位置及 alpha mode。未知名称为 `null`，不会猜测 BT.709 或 BT.2020。
- mastering display 和 content-light 从实际帧 side data 读取。mastering 的存在、primaries、luminance 分别判断，部分缺失保持未知；MaxCLL／MaxFALL 的零值表示未知。元数据比值使用精确有理数。
- `SideDataTypes` 记录帧携带的数据类型，不代表已解析或保证可以重新写出全部 payload。Dolby Vision、HDR10+ 动态元数据处理不在本步范围。

## 像素与资源

`DecodedVideoFrame` 持有独立 `AVFrame` 引用，可以跨后续读帧及 decoder 释放继续使用。`Info` 在帧释放后仍可读取；平面查询和复制在释放后抛出 `ObjectDisposedException`。

`GetPlaneInfo()` 返回原始 signed stride、有效行字节数和行数。`CopyPlane()` 逐行复制到独立、紧密排列的 `byte[]`，只复制有效像素，不复制行 padding。负 stride 保持逻辑行顺序；单行调色板允许零 stride。返回数组由调用方拥有，修改它不会改变原生帧。原生端在复制前验证平面位于实际持有的 AVBuffer 内。

复制不应用裁剪、不转换像素格式、不改变位深、不执行 tone mapping。YUV／RGB 数据和色彩事实一同供后续转换使用，不能把原始 plane 当作 Avalonia 可直接显示的 RGBA。当前保留 10-bit 输入仅证明解码未降位深；HDR 合成、编码和成片重新解码验收尚未完成。

读取是同步调用，应由后续媒体工作线程调度。打开／读取／释放按实例串行；`Cancel()` 可从其它线程调用，设置原生原子取消状态，并由 FFmpeg I/O 中断回调及解码循环检查响应。取消为协作式，不承诺抢占任意 codec 内部计算。已开始的取消是终端状态，需要新建 decoder；预先取消的 token 在进入原生操作前抛出，不会取消已打开的会话。

托管 `Dispose()` 先请求取消，再等待正在执行的读取退出并释放 SafeHandle。返回的帧由调用方逐一释放，不由 decoder 回收。原始 C ABI 调用方必须自行遵守串行释放约定，不能在读帧期间直接 destroy。

```csharp
using var decoder = FfmpegVideoDecoder.Open(localFilePath, videoStreamIndex, cancellationToken);
while (decoder.ReadFrame(cancellationToken) is { } frame)
{
    using (frame)
    {
        var timing = frame.Info.PresentationTimestamp;
        var layout = frame.GetPlaneInfo(0);
        var pixels = frame.CopyPlane(0);
    }
}
```

## ABI 与版本

ABI 1 使用 exact size／version 的固定宽度结构、UTF-8 路径及错误文本、显式 Windows cdecl。C 和 C++ 用 static_assert 验证布局，托管测试用 Marshal 验证大小及偏移。原生异常在 C ABI 边界转换为错误码；句柄以 SafeHandle 释放。

Step 1.7 追加 `an_decode_features`、`an_decoder_get_time_base` 和 `an_decoder_seek`，既有结构体布局保持不变。SEEK capability 表示时基查询与 seek 两个入口都存在；托管打开时检查该能力，旧库会明确要求重新构建 Decoder，不能仅因 ABI 仍为 1 就视作具备新功能。

编译头文件和实际运行库必须同时匹配共用 `ffmpeg-toolchain.json`：FFmpeg 9.0.2，libavformat／libavcodec 63.1.102，libavutil 61.1.102。允许的发行字符串包含 Scoop shared 发行的 Gyan 后缀。`GetBackendInfo()` 实际加载库并核验版本；构建脚本的环境检查不能代替此运行时检查。

## 定位与显示帧选择

`FfmpegVideoDecoder.StreamTimeBase` 查询实际选定流的时基。`SeekToKeyFrame(MediaTime)` 按该时基向下取整，调用原生 `av_seek_frame` 的 BACKWARD 关键帧定位；保持绝对来源时间，不减容器 start time。成功后清空 codec、pending packet 和 scratch frame，复位 demux／drain／decoder EOF 状态。正常 EOF 可以重新 seek；取消或实际底层 seek 失败的 decoder 不能恢复。未定义时间戳哨兵和换算溢出在执行定位前拒绝。[FFmpeg 9.0.2 定位与 flush 契约](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libavformat/avformat.h#L2255-L2297)

关键帧定位不保证目标时刻的第一张显示帧。`VideoFrameNavigator` 拥有解码器工厂、有限 lookahead 和原始帧区间缓存，继续解码取得真实下一帧 PTS，以半开区间 `frame.Time <= target < frame.NextFrameTime` 选择画面。精确命中下一帧起点时选下一帧；比较使用有理 `MediaTime`，VFR 不先转换为浮点秒或毫秒。

重复 PTS 的同时间组选择最后解出的帧，其余帧立即释放。缺失原始 PTS／时基或显示顺序中的时间倒退会报 `InvalidDataException`，不自动使用 best-effort、不排序或猜测帧率。末帧以 `NextFrameTime = null` 和 `ReachedEnd = true` 表达，没有制造无限或任意有限结束时间。

首次定位先从实际文件起点确认首帧时间。目标早于首帧时优先复用已知首帧，否则重新打开起点，返回首帧并标记 `IsBeforeFirst`；目标超过末帧时返回末帧。若 demux seek 落点晚于目标或直接落到 EOF，则重新打开文件并顺序扫描确认正确画面，不能把较晚落点误当文件开头。该回退可能扫描较长内容；完整帧索引仍未建立。

`PositionedVideoFrame` 持有唯一的 `IVideoFrame` 释放责任。读取下一帧、seek 或关闭 navigator 都不会释放已经交给调用方的帧；navigator 释放自身 lookahead、缓存引用和 decoder；已交付的租约继续保留底层帧，直到调用方释放。工厂每次必须返回新打开的 decoder。进行中取消会终止当前原生操作，普通暂停或请求替代不使用该取消路径；独立播放会话的规则见 [播放基础](video-playback.md)。

## 拖动定位与帧缓存

已解码区间优先通过有界 LRU 缓存复用，正向和反向命中都不再重复 GOP 解码。缓存默认最多 120 帧与 256 MiB，按各原始平面 `max(abs(NativeStride), RowBytes) * Height` 计预算；超出单帧预算时不入缓存。此预算不包含解码器内部缓冲、预览转换数组和外部持有的帧租约。缓存不复制像素或做 SDR 转换，保留原始位深、色彩与 HDR 元数据。

缓存命中后的顺序播放按逻辑下一 PTS 读取，耗尽后准确续接实际解码器；不能直接跳到物理读头。未命中时，距离 lookahead 不超过 250 ms 的向前目标直接顺读，其余仍使用关键帧定位和精确扫描。`IVideoFrameSource` 的请求替代入口在原生帧读取之间检查 generation；这是非终止的请求替代，不调用 sticky 原生取消。关闭和显式取消保留原有终止语义。

## 验证入口

普通 Media 测试不要求解码库，会显式跳过原生集成用例。启用以下环境变量后，缺少库、工具路径或版本不符都使测试失败，不会继续跳过：

```powershell
$env:AEGINEXT_RUN_DECODER_TESTS = '1'
# 替换为所选 shared SDK 内工具的完整路径。
$env:AEGINEXT_FFMPEG_PATH = '/opt/homebrew/opt/ffmpeg/bin/ffmpeg'
$env:AEGINEXT_FFPROBE_PATH = '/opt/homebrew/opt/ffmpeg/bin/ffprobe'
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Decoding'
```

原生 CTest 检查平面边界、负 stride、奇数尺寸 10-bit 色度平面、调色板、时间与 HDR 部分字段、C ABI 和资源生命周期。托管集成生成短 PQ／HLG／BT.2020 SDR HEVC 素材，包含音频前置流、B 帧及真实 VFR；逐帧与 FFprobe 时间戳对照，并与 FFmpeg 原始解码像素逐字节比较。还检查跨 decoder 释放的帧保留、拷贝独立性、取消和错误路径。

最终执行结果与日志路径见 [Checkpoint](README.md#实施与验收记录)。这些测试尚未证明播放器实时性能、Windows 可运行性或 HDR 压制保真。
