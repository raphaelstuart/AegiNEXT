# 预览解码切换与缺失颜色标签验收

[English](../../checkpoints/video-decode-modes-and-missing-color.md) | [简体中文](video-decode-modes-and-missing-color.md)

## 完成行为

本次修复覆盖缺少范围、矩阵、原色和传递函数标签的 SDR H.264，包括报告中的 High profile、1280×720、60000/1001 fps、AAC LC 双声道 44100 Hz 输入类型。原生颜色解释获得有效 SDR 参数，原始事实继续保持 unknown；预览和导出使用同一政策。显式不支持或冲突的 HDR、film-grain 元数据仍拒绝处理。

设置 → 媒体 → 预览解码方式提供自动、CPU、GPU，持久化选择、立即切换语言，并显示实际解码器和回退原因。视频已打开时，切换须取得转换后的预览帧才确认设置；失败恢复旧解码器、时间位置和播放状态。Epoch 检查阻止迟到帧及旧回滚覆盖新打开的视频。个人偏好不修改字幕项目及其 Undo 历史。

预览和导出静态链接 `native/shared`，分别持有会话。Auto 仅在交付第一帧之前遇到已确认硬件错误时重开软件解码；损坏输入、I/O、取消、分配失败和交付之后的错误不触发回退。导出解码方式与预览偏好、编码方式独立。导出 ABI 3 返回实际解码和有效输出颜色，最终 mux 使用有效标签校验。

首版硬件支持 VideoToolbox 和 D3D11VA 的不透明 4:2:0 H.264 8-bit、HEVC 8/10-bit。下载保留 NV12/P010 精度及真实帧属性。软硬件可能返回不同编码高度和裁剪布局，等价测试比较可视样本、时间、位深及颜色/HDR 元数据，不伪造已经不可访问的 padding。

真实回归同时修复了 Auto 重开后的借用 stream 指针失效，以及 NUT 中间流 mux 至 MP4 时最后帧 duration 缺失。导出验收比较可视像素、固定色块、全部帧 PTS 和复制的 AAC packet hash，不仅检查文件是否生成。

## 自动化证据

执行记录保存在 `artifacts/verification/`；媒体核心故障注入记录位于 `artifacts/media-core-review/`。构建、测试分别记录，存在重叠的定向运行不能相加作为唯一覆盖数量。

| 定向范围 | macOS ARM64 | ARM64 虚拟机中的 Windows x64 |
|---|---:|---:|
| Debug 媒体解码/预览 | 119/119 | 123/123，包含选定导出用例 |
| 预览控制器、偏好和本地化 | 59/59 | 59/59 |
| 设置 UI | 13/13 | 13/13 |
| Release ABI 和解码模式 | 16/16 | 16/16 |
| 原生解码/导出，各 Debug、Release | 4/4 + 2/2 | 4/4 + 2/2 |

以上测试运行均报告零跳过。Windows session 测试不能进入硬件成功交付后的分支，该分支已在 macOS 实际执行。独立导出 ABI/wire/worker/颜色回归通过 60/60；最终全部导出用例与解码/导航生命周期联合回归在 macOS 通过 40/40。两平台 Workbench 的 Debug、Release 构建均成功。

- macOS ARM64 的 Debug、Release 均编译共享解码/导出核心。原生测试覆盖 ABI、平面、颜色依赖和可控解码故障。交付之后禁止回退的故障注入实际使用 VideoToolbox 会话；没有 fixture 时明确标记 CTest skip。
- 软件/真实 VideoToolbox 覆盖 720p、带裁剪 1080p 的 H.264/HEVC 10-bit，可视 sample 相等、EOF、seek、mastering/content-light side data 和原生存活计数。未标记预览与压缩像素完全相同、SPS 显式 BT.709 的参考视频比较。
- 导出覆盖软件/真实硬件解码、透明和半透明叠加、有效标签校验、原始 unknown 不变、帧数/PTS/duration、AAC packet copy、兼容的中途标签及真实色彩变化的原子失败。
- 控制器、偏好和实际 Avalonia headless UI 覆盖播放中切换、首帧转换失败、回滚与新打开竞争、语言切换、持久化选择、busy 状态、实际后端说明和设置窗口清理。
- Windows 11 通过 ARM64 仿真执行 x64 原生库及 worker，使用 FFmpeg 9.0.2 和 Parallels WDDM 驱动。该设备没有可用的视频解码 GUID；Auto 回软件、强制 GPU 返回硬件协商错误。已执行 Windows 软件编解码及回退，不能据此声称物理显卡上的 D3D11VA 成功。
- 托管构建零警告、零错误。Rider 唯一 error-level 结果为既有 `VideoExporter.ExportAsync` 的 CA1822，构建遵守其服务实例抑制。macOS 原生编译使用 `-Werror`；链接仍提示本机 Homebrew FFmpeg 要求 macOS 27，而项目目标为 14。

## 性能证据

同一个 4K VFR 长 GOP 源、四轨动画字幕，各模式执行五轮，交互输出 960×540。每轮首个 target 不计入 warm 分位，共 155 个 warm 样本在 4–7 秒间交替 seek。包含真实解码、SDR 转换及 CPU 场景合成，不包含 UI 上传。

| 交替 seek | 总耗时 P50 | 总耗时 P95 | Codec P95 | 下载 P95 |
|---|---:|---:|---:|---:|
| 修复前软件基线 | 291.5 ms | 370.9 ms | 未采集 | 0 ms |
| 软件 | 280.2 ms | 353.5 ms | 308.0 ms | 0 ms |
| 自动，确认 VideoToolbox | 468.3 ms | 573.7 ms | 469.9 ms | 52.8 ms |
| 强制 GPU，确认 VideoToolbox | 459.1 ms | 571.1 ms | 466.9 ms | 54.6 ms |

CPU 总耗时 P95 变化为 −4.69%，在允许 10% 回归的界限内。硬件在这个反复执行长 GOP preroll 的负载上更慢，不能据此承诺拖动更快。Codec/download 是 API 阶段计数，异步 GPU 工作可能在下载阶段完成。

交替 seek 的对照全程使用 Debug。另以最终核心的 Release 执行 4–5 秒间连续 target，各五轮：CPU 总耗时 P50/P95 为 39.2/41.6 ms，Auto 为 41.8/45.1 ms，强制 GPU 为 42.2/45.4 ms。Auto/GPU 每轮均确认实际 VideoToolbox，三种模式初始化 P95 均约 11 ms。配置及导航方式不同，不合并计算回归百分比。阶段耗时和会话证据保存于 `video-decode-performance-summary.json` 及六份分模式 JSON 报告。

## 验收边界

未提供原始视频文件，附件仅包含播放器显示的属性；自动 fixture 重现此输入类型。原文件交互、物理 Windows GPU、Linux、macOS 最低系统部署和原生 HDR 显示仍是独立验收项。本次没有发布包或项目格式变更，不自动关闭或重启现有工作台。
