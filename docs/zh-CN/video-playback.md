# 视频播放基础

[English](../video-playback.md) | [简体中文](video-playback.md)

Phase 1 / Step 1.7 在 `Media/Playback` 建立独立播放会话，使用 `Media/Decoding` 的原始帧源与精确定位。模块不引用 Avalonia，不包含显示转换、音频时钟、变速或循环播放；以上为原始模块阶段边界；当前工作台已集成播放、音频、颜色转换和独立导出，见 [工作台手册](workbench.md)。

## 会话与命令

`VideoPlaybackSession` 接收帧源工厂、可选 `TimeProvider` 和显示队列容量。工厂、读取和定位都由同一个后台工作循环执行。`OpenAsync(path, streamIndex, ...)` 是本地文件便利入口；工厂构造方式用于其它帧源和确定性测试。

| API | 完成语义 |
| --- | --- |
| `OpenAsync` | 源打开、取得首帧并进入暂停；空源进入结束状态。 |
| `PlayAsync` | 按当前精确媒体位置建立时钟锚点；结束状态不自动回到开头。 |
| `PauseAsync` | 冻结提交命令时的位置，定位该暂停画面并交付。 |
| `SeekAsync` | 选择目标时间对应的显示帧并暂停；结果包含请求／所选时间、下一帧边界、代次和起点／EOF 状态，不复制帧所有权。 |
| `ReadPresentationAsync` | 取得一次可释放的显示交付；空队列时等待，正常结束／关闭返回 null，故障传播异常。 |
| `Snapshot` | 读取状态、当前媒体位置、显示帧时间、代次及故障的不可变快照。 |
| `CloseAsync`／`DisposeAsync` | 请求终端取消、唤醒等待、释放未交付帧和源，并等待工作循环退出；允许重复调用。 |

暂停和普通 seek 不调用解码器终端 `Cancel()`。命令 token 可以撤回尚未开始的请求；命令已经执行时，该 token 不再中断共享帧源，调用方仍得到执行结果。要停止正在进行的原生操作，应关闭会话。消费者等待的 token 只取消自身等待。

较新的 Pause／Seek 提升请求代次，清除尚未交付的旧帧。旧定位在原生调用返回后核对代次，释放旧结果并以取消完成，不能覆盖新位置。已执行的源读取无法回滚时，会重新定位同步源游标；请求替代不永久取消共享解码器。

普通帧区间中的 seek 保留请求位置，例如目标 50 ms、显示帧起点 40 ms，恢复播放从 50 ms 开始。目标早于文件首帧或落在已确认的末帧时，位置钳制到该边界帧。末帧没有真实下一帧时间，当前会话在末帧起点停止，不通过 fps 猜测最后一帧持续时间。

## 时间与调度

当前位置由单调时间戳差值和 `TimeProvider.TimestampFrequency` 直接构造有理 `MediaTime`，与时钟锚点相加；不累计定时器预期间隔，也不使用系统日期时间驱动播放。仅安排等待时按 CEILING 量化为 `TimeSpan`，到期后重新核对实际媒体位置。[TimeProvider 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider.gettimestamp?view=net-10.0)

帧使用真实 PTS 的半开显示区间。精确到达下一帧时间时交付下一帧；工作循环落后跨越多帧时，释放已过期帧，交付当前时间所在的画面。暂停后时钟固定，恢复时从冻结位置建立新的锚点。帧选择、VFR、重复或未知 PTS 的具体规则见 [视频解码](video-decoding.md)。

## 队列与所有权

显示队列容量由构造参数指定，默认 2。队列满时释放最旧的未交付帧，为当前结果腾出空间，不等待消费者，从而允许定位和关闭继续处理。读取时也会释放已经超过真实下一帧边界的交付，避免慢消费者显示过期画面。

队列内的 `VideoPresentation` 由会话拥有；成功读取后由消费者唯一拥有并负责 `Dispose()`。会话关闭不会回收已经交给消费者的帧。显示交付记录 `Generation`，消费方在提交绘制前必须与当前快照代次核对；工作循环内部丢弃旧结果不能阻止消费方已经取出的旧帧稍后写回画面。

`ReadPresentationAsync` 返回 null 表示当前结束或关闭，没有把 EOF 永久完成为不可重用的通道。EOF 后可以重新 Seek，再读取新的显示交付。发生源错误会保存故障、结束工作循环并回收内部资源；错误不能伪装为正常 EOF。

```csharp
await using var session = await VideoPlaybackSession.OpenAsync(localFilePath, videoStreamIndex);
var result = await session.SeekAsync(targetMediaTime);
using var presentation = await session.ReadPresentationAsync();
if (presentation is not null && presentation.Generation == session.Snapshot.Generation)
{
    var rawFrame = presentation.PositionedFrame.Frame;
    var timestamp = presentation.PositionedFrame.Time;
}
```

本步交付的是媒体控制和原始帧调度。YUV plane 尚不能直接交给 Avalonia 显示，HDR 输入也未在本步转为 SDR；预览转换不会作为 HDR 导出的源。

## 验证范围

确定性测试使用可控帧源、阻塞定位和手动单调时钟，核对边界、暂停／恢复、慢消费者、跨帧追赶、请求替代、取消、故障与关闭释放。真实素材测试使用多 GOP／B 帧的固定及可变帧率视频、非零起始时间，对照 FFprobe 时间与 FFmpeg 原始像素，并检查会话交付／定位／关闭的接线。

最终执行结果见 [Checkpoint](README.md#实施与验收记录)。Windows 实机、实时性能、长片索引、音视频同步、桌面交互及最低系统兼容性仍需后续验收。
