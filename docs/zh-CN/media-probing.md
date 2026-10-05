# 媒体信息探测

[English](../media-probing.md) | [简体中文](media-probing.md)

Step 1.5 提供独立的 FFprobe 进程适配器，读取本地文件的容器及全部流信息。它是正式的只读输入适配层；后续原生解码、预览和导出消费同一组 Core 契约。以上为原始模块阶段边界；当前工作台已集成播放、音频、颜色转换和独立导出，见 [工作台手册](workbench.md)。

## 依赖与调用

`src/AegiNext.Media/Probing/ffmpeg-toolchain.json` 是脚本与运行时共用的版本契约，作为资源嵌入 Media 程序集。当前固定 FFmpeg/FFprobe 9.0.2、libavutil 61.1.102、libavcodec/libavformat 63.1.102，接受裸版本和白名单中的 Gyan 完整构建发行字符串。其它版本明确失败，不自动升级、降级或改用另一份 PATH 工具。

```powershell
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -WithMediaTools -InstallDependencies
```

普通 Managed 构建不要求媒体工具。`-WithMediaTools` 显式启用 FFprobe 与 FFmpeg 检查，后者用于生成集成测试素材及后续媒体工作；探测 API 本身只执行 FFprobe。安装仍只在 `-InstallDependencies` 下使用 macOS Homebrew 或 Windows Scoop，规则见 [构建说明](building.md)。

应用层向 API 传入完整可执行文件路径，发布时应由按 RID 管理的工具包提供此路径。本步尚未实现发布包下载、哈希白名单、签名或许可证交付。开发时 macOS 可使用 Homebrew 实际工具路径；Windows 可从 `scoop prefix ffmpeg` 找到 `bin/ffprobe.exe`，不要把 Scoop shim 当作待发布工具本体。

```csharp
var probe = new FfprobeMediaProbe(new FfprobeOptions(ffprobeExecutablePath));
var report = await probe.ProbeAsync(mediaFilePath, cancellationToken);
var streams = report.Asset.Streams;
```

一次调用先验证工具身份，再读取媒体；媒体报告再次带版本信息，前后核对工具文件 SHA-256。编译头版本与实际运行库 packed version 都参与检查，报告保存配置路径、文件哈希、报告的运行库版本与 stderr 诊断。哈希用于识别本次配置文件，不替代发布来源与依赖库签名验证。

## 事实与未知值

Core 不引用 JSON、进程或 FFmpeg API。`Core/Media` 模型使用不可变集合与初始化属性保存探测快照，外部自行构造的快照不等于已验证媒体；输入校验在适配器完成。

| 数据 | 契约 |
| --- | --- |
| 流 | 保留所有绝对索引、编码类型、编码名、标签和 disposition，包括附图、多个 default 或不连续索引。探测器不替调用方选轨。 |
| 时间 | 每个流保留独立的原始 PTS、时基和报告时长刻度。`StartTimestamp` 仅在 PTS 和时基都已知时成立。音视频不各自归零。 |
| 报告秒数 | 容器与流的十进制时间直接转为 `MediaTime` 有理数，不经过 double；与原始刻度分开保留。报告时长可能是估计值，不能宣称是精确片尾。 |
| 比值 | `MediaRatio` 表达规范化的精确数值。SAR、报告帧率与平均帧率分别保存；两种帧率相同也不意味着 CFR，不能代替逐帧 PTS。 |
| 色彩 | 保留 range、matrix、transfer、primaries、chroma location 的来源字符串。未知名称不改写为 BT.709；`IsPq` / `IsHlg` 仅表示传递函数信号，不等于完整 HDR10 合规。 |
| HDR 元数据 | 保存流级 mastering display 的色度及 cd/m² 亮度、CLL/FALL，并允许部分字段缺失。CLL/FALL 的零按未指定处理；不存在的值不填造。它们不直接作为渲染参考白或帧像素峰值。 |
| 视频与音频 | 保存画面尺寸、像素格式、报告原始位深、SAR、显示矩阵文本／旋转角度；音频保留采样率、声道数和布局，不按声道数猜布局。 |

缺字段、`N/A`、未定义 PTS 哨兵及适用的未知比值保留为 null；畸形数字、重复索引、重复字段、非法符号或无法精确表示的数值明确失败。单个数值文本限制 256 字符，避免异常元数据触发无界大整数计算。

## 进程与覆盖边界

- 只接受存在的本地文件，FFprobe 协议限制为 `file`；网络输入留待独立设计。
- 使用 `ArgumentList`，不经过 shell。stdin 关闭，stdout/stderr 并发排空。
- 每次工具调用默认超时 30 秒；默认 stdout 最多 16 Mi 字符、stderr 最多 64 Ki 字符。取消、超时、输出超限终止仍运行的进程树并等待直接启动的 FFprobe 退出。调用契约要求直接使用 FFprobe，不支持派生后台进程后自行退出的包装器；整组后台进程所有权需要后续专门的进程组／Job 实现。非零退出码即使带有部分 JSON 也失败。
- `probesize` 为 10 MiB，`analyzeduration` 为 10 秒；不启用全片 `show_frames`、`show_packets` 或计帧。因此这是有限探测，不是完整帧索引或全片元数据扫描。
- 流级没有 HDR side data 只能说明本次探测未报告。帧内或动态 HDR 元数据可能尚未读取；未知 side data 保留类型名称，首版暂不实现其完整载荷模型。
- 真实 PQ／HLG／BT.2020 SDR 探测测试只验证输入事实读取。测试素材由 FFmpeg 生成，不能当作 AegiNext 已完成 HDR 合成、亮度保真或压制导出的证据。

本机 FFmpeg 9.0.2 的 fixture 验证发现：只给输出选项和 x265 VUI 设置 primaries／transfer 时，生成素材仍报告未知；通过 `setparams` 同时设置输入编码器的帧色彩后才得到预期信号。另一份含 mastering／CLL 的实际素材，其流级报告为空，而首帧包含元数据。这些结果要求后续解码／合成／编码完整传递帧色彩与 side data，并用成片重新解码验收，不能只检查命令行参数。

这些边界依据 [FFprobe 官方说明](https://ffmpeg.org/ffprobe.html)、[AVStream 时间定义](https://github.com/FFmpeg/FFmpeg/blob/master/libavformat/avformat.h) 和 [HDR 静态元数据定义](https://github.com/FFmpeg/FFmpeg/blob/master/libavutil/mastering_display_metadata.h)。

## 定向验证

普通 Media 测试中的真实工具用例会显示明确的跳过原因。要执行它们，先通过 `-WithMediaTools -CheckEnvironment`，再提供实际工具路径：

```powershell
# macOS Homebrew；Windows 可用 Join-Path (scoop prefix ffmpeg) 'bin/ffprobe.exe' 等实际路径。
$env:AEGINEXT_FFPROBE_PATH = (Get-Command ffprobe).Source
$env:AEGINEXT_FFMPEG_PATH = (Get-Command ffmpeg).Source
dotnet test Tests/AegiNext.Media.Tests/AegiNext.Media.Tests.csproj -c Release --filter 'FullyQualifiedName~Probing'
dotnet test Tests/AegiNext.Core.Tests/AegiNext.Core.Tests.csproj -c Release --filter 'FullyQualifiedName~MediaRatioTests'
```

集成用例实时生成极短 10-bit HEVC + PCM 音频素材，使用包含中文、空格、引号和 shell 特殊字符的路径；完成后删除素材。测试日志保留探测报告。`Tests/AegiNext.Media.TestHost` 是用于参数、双管道、超限、取消和超时测试的确定性子进程，不随桌面应用引用或发布。
