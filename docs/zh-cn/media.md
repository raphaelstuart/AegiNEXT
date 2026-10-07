# 媒体集成

[English](../en/media.md) · [简体中文](media.md) · [全部指南](README.md)

## 配置与入口

构建匹配 RID/配置的 `Workbench` 原生库，详见[构建](building.md)。开发运行使用绝对 `AEGINEXT_FFPROBE_PATH`/`AEGINEXT_FFMPEG_PATH` 或 PATH，显式路径无效即失败；应用包使用包内工具。

| 位置 | 职责 |
|---|---|
| `src/AegiNext.Media/Probing` | FFprobe 身份、有界进程与不可变媒体事实 |
| `src/AegiNext.Media/Decoding` | 解码/帧契约、原生句柄和精确帧导航 |
| `src/AegiNext.Media/Playback` | 原始帧调度与呈现所有权 |
| `src/AegiNext.Media/Preview` | 独立 SDR 转换 |
| `src/AegiNext.Media/Audio`、`Analysis` | 输出时钟与视口音频分析 |
| `src/AegiNext.Desktop/Controllers`、`Workspace` | UI 交付、播放、校准和分析协调 |
| `native/shared` | 预览/导出共享解封装、解码、定位和颜色解释 |

## 探测与解码

使用 `FfprobeMediaProbe` 读取已有本地文件：验证工具身份、不经 shell 调用、并发读取双管道，并限制超时/输出。保留全部流索引、原始时间戳/时间基、颜色/HDR 事实及未知值；报告帧率不能证明 CFR。

通过 `FfmpegVideoDecoder` 打开绝对流索引，优先接收缓存帧并排空 EOF。返回帧拥有独立引用，后续读取或解码器释放不回收它；每帧显式释放。平面复制按逻辑行顺序保留有效像素，不裁剪或转换颜色。

先定位关键帧，再选择真实 PTS 区间 `Time <= target < NextFrameTime`。VFR 使用有理时间；缺失/倒退 PTS 拒绝，重复 PTS 选择最后解码帧。缓存限制帧数/字节预算，命中后仍须将顺序播放连接到逻辑下一帧。

自动解码优先 macOS VideoToolbox 或 Windows D3D11VA，可在首次交付前回退。强制 GPU 验证真实加速并报告失败；取消、损坏输入和后续错误不静默回退。硬件回读保留位深和帧元数据。

缺失 SDR 标签使用共享的显式解析规则，原始事实不变，解析结果标明推断字段。存在 HDR 证据时要求完整受支持标签。预览解码偏好与导出解码/编码选项独立。

## 统一时间源

默认音频使用 macOS CoreAudio、Windows WASAPI，输出 48 kHz 双声道 float PCM，预填约 200 ms、队列最多 250 ms。设备呈现位置映射已消费样本，SYSTEM 时间不减去猜测的队列/设备缓冲延迟。

额外延迟校准按设备、后端、采样率和声道匹配。设备时钟丢失时冻结打轴并更换输出，F8/F9 不能使用不可用时钟；音频空隙/短轨道补静音。无音频视频使用单调时钟，显式 SDL 输出标为 ESTIMATED。

播放结果包含 generation，新命令使旧交付失效，消费方在呈现前检查身份。暂停/替换取消过期工作而不终止共享解码器；关闭请求原生取消、等待工作结束并释放未交付资源，已交付帧仍由消费方负责。

## 预览合成与音频分析

SDR 转换借用原始帧，输出独立、不透明、从上到下的 sRGB BGRA8；导出不使用显示像素。合成保留对应的未叠字背景，避免重复字幕或错帧背景。

控制器结果检查请求、媒体时间、工程修订和画质。工作台选择预览画质，交互时临时限制。呈现帧与派发延迟应和解码耗时分开测量，尤其是长 GOP、高分辨率和反复定位。

分析使用独立固定网格：48 kHz 单声道 min/max 波形，低通至 16 kHz、64 ms FFT、16 ms hop 的语谱。按视口请求 tile、有界预取/缓存，各图层共享 PCM 并保留源时间。颜色/播放头刷新复用几何；隐藏、更换媒体或关闭取消过期分析，不占用播放音频。

## 验证

```powershell
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj -c Release --filter 'FullyQualifiedName~VideoPreview'
```

原生用例所需开关和工具路径见[构建](building.md)。验证实际 PTS/像素、定位、替代、取消、帧生命周期和故障。静音设备测试不证明扬声器延迟，Headless 呈现不证明原生 HDR 或 GPU 验收。
