# 视频导出

[English](../en/export.md) · [简体中文](export.md) · [全部指南](README.md)

## 导出视频

1. 打开**压制**面板，选择自动、H.264 或 HEVC。
2. 选择 CPU 编码（速度/CRF）或 GPU 编码（码率），再选择复制源音频、AAC 或无音频。
3. 点击**导出**，选择尚不存在的 MP4 或 Matroska 文件。
4. 查看进度，必要时取消，完成后检查输出。

导出在开始时捕获工程，通过独立 `aegn-exporter` 进程执行；后续编辑不影响任务。成功时原子提交新文件，失败/取消只清理自己的临时输出；已有目标文件会被拒绝。

## 选择 CPU 或 GPU

| 模式 | 参数与范围 |
|---|---|
| CPU，默认 | libx264/libx265、速度与 CRF；HDR 必须使用 |
| GPU | 0.1–200 Mbps，默认 8 Mbps；SDR H.264 8-bit 或 HEVC 10-bit |

macOS 使用 VideoToolbox，Windows 尝试受支持的 NVENC/QSV/AMF。硬件、驱动或编码器不可用时报告错误，关闭 GPU 可使用软件编码。GPU 加速视频编码，字幕合成仍采用线性高精度链路。

## HDR 与输入要求

编辑预览为 SDR；PQ/HLG 导出读取原始高精度帧并使用软件 HEVC 10-bit，HDR H.264 和 GPU HDR 会被拒绝。

- PQ 字幕参考白默认 203 nits；HLG 使用 1000 nits、零黑位参考条件。
- 保留完整静态 HDR10 mastering；合成后 MaxCLL/MaxFALL 设为未知 0/0。
- Dolby Vision/HDR10+ 等动态 HDR、变化/不完整 mastering 和不支持的色彩语义会被拒绝。
- 输入须为受支持的整数平面 YUV、方形像素，画布与可见视频尺寸一致；VFR 保留源呈现时间戳。
- 旋转、立体、ICC、交错、损坏/动态格式，以及封装链路不支持的负时间戳会报告错误。

复制音频保留包内容，AAC 使用选定码率。容器/时间戳不兼容时提供诊断，不静默移动源时间线。

ASS/SRT 文件见[字幕编辑](subtitle-editing.md)，应用包要求与校验见[发布](publishing.md)。
