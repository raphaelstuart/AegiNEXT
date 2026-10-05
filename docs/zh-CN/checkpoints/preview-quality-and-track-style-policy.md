# 预览画质、安全轨道样式与双语文档

[English](../../checkpoints/preview-quality-and-track-style-policy.md) | [简体中文](preview-quality-and-track-style-policy.md)

日期：2026-10-05。产品版本保持 **0.1.0**，工程格式保持 **v3**。先将上一轮生产修改提交为 `ea4a5ce`（`feat: add track style presets and karaoke appearance`），共 29 个源码文件，未包含测试或文档。本页对应的新修改保留在工作区，无关 native 测试修改已保留。

## Phase 1：提交范围与接口

Checkpoint：核对暂存内容，仅提交上一轮生产源码，区分个人预览偏好与工程轨道样式。已完成：提交只包含 `src/` 生产文件，开始新修改时暂存区为空。

## Phase 2：预览画质

Checkpoint：画质实际作用于转换与合成，作为个人设置保存，并保留播放与选择状态。

- 稳定标识 `LOW`、`STANDARD`、`HIGH` 对应 960×540、1280×720、1920×1080 输出上限。默认流畅 540p；缺少新字段的旧偏好同样使用低画质，读取时不重写文件。
- 选择器位于 SDR 标识旁，未加载媒体也可选择；语言切换保留稳定选择，小视频不放大。
- 原始背景转换和场景合成都遵守像素预算。交互预览最高 540p，最终定位恢复所选画质；压制继续使用原质量链路。
- 画质 revision 使缓存和在途结果失效。播放中切换不暂停、不寻址；暂停时刷新当前位置。各档 native 转换器复用，并由所属对象统一释放一次。
- 真实 FFV1 素材覆盖解码、native SDR 转换、合成、尺寸、源帧不变、交互后恢复和低分辨率行为。素材包含完整 BT.709、范围与色度采样位置元数据。

## Phase 3：轨道样式

Checkpoint：禁止隐式全部应用；只有用户明确选择才修改本轨已有片段。

- 轨道名称下显示样式名称标识，绘制与命中共用轨道头几何；关闭自动应用时标识变淡，点击仍选择所属轨道。
- 轨道右键切换自动应用。开关保留已存默认样式和已有片段；关闭时，新字幕采用样式面板当前所选预设，没有选择时采用基础样式。
- 对含字幕的轨道选择预设后，显示可等待的“是／否／取消”对话框。默认“否”：只更新后续创建默认值。“是”同时更新本轨已有片段；取消、Esc 或关闭窗口均不改工程。空轨无需确认。
- 应用层 API 默认 `updateExisting: false`；全部轨道应用的 API、命令、文案及菜单均已删除。
- 新建、打轴和导入复用已准备资源的一次事务。等待字体准备前固定轨道、预设和时间；准备期间收到打轴结束命令，保留结束时间，创建成功后应用。
- 工程 v3 新增可选 `autoApplyStyle`，此前 v3 缺字段时默认为 true。Undo、图层／效果身份、拆分和跨轨时保留原片段样式，以及碰撞验证继续生效。

## Phase 4：文档与 Skill

Checkpoint：完整双语对应版本、可用语言／资源链接，以及符合项目边界的 Skill。

英文文档保留现有 `docs/` 路径，中文对应版本位于 `docs/zh-CN/`。根目录、布局层和字体素材的 README 均增加 `README.zh-CN.md`。旧六面板／工程 v2 描述明确标记历史范围，当前指南使用七面板／工程 v3。原先指向不存在 `plans/*.md` 的断链已替换为当前文档。

项目 Skill 位于 `.agents/skills/aeginext-effect-dsl/` 和 `.agents/skills/aeginext-controls/`；本机发现入口链接到同一份源码。每项均有专用定义、界面元数据和参考文件，均通过 skill-creator 校验。50 份文档／README（22 对文档及三对其他 README）已检查本地文件、标题锚点、语言切换及同语言链接，共 311 个本地链接，无失败（`bilingual-docs-links.json`）。独立试用另写颜色／向量脚本，由真实解析器和编译器验证 12 份源代码、每份七种时长，含 1µs、短片段、精确边界和非零内容偏移，共 **84 组检查通过**。

## Phase 5：验证

| 范围 | macOS | Windows x64 |
|---|---:|---:|
| Core DSL／轨道 | 57 通过 | 57 通过 |
| Application 轨道事务／预设 | 34 通过 | 34 通过 |
| Desktop 偏好／调度／真实转换／工作流 | 41 通过 | 41 通过 |
| Headless UI 选择／确认／快捷键／画质 | 42 通过 | 42 通过 |
| 真实独立 worker／协议／字幕输出 | 3 通过 | 3 通过 |

macOS：**177 个不重复测试通过，无跳过**。Desktop 首轮 38 项通过，三项暴露素材问题；补齐视频元数据并改用已有授权 Noto 字体后，三项定向重跑全部通过。测试中的 xUnit v2 API 和分析器错误在执行前已修正。生产 Release 构建零警告、零错误；受影响生产及测试文件通过 Rider 错误级分析。测试关闭所属窗口并释放会话。

证据位于忽略目录 `artifacts/verification/`：`styles-quality-core-macos.trx`、`track-style-options-application-macos.trx`、`preview-quality-desktop-macos.trx` 及 `preview-quality-desktop-macos-retry.trx`、`styles-quality-ui-macos.trx`、`styles-quality-media-macos.trx`、`skill-forward-check-result.json`。Parallels 中的 Windows x64 对最终源码执行同范围 177 项测试，无跳过，结果为 `styles-quality-windows-*.trx`。最新结果合并审计为 `styles-quality-test-summary.json`。

真实触控板、跨 DPI、窄 Dock 的视觉及实际播放流畅度仍需人工验收。像素预算验证不能证明 FPS 提升。本轮验证源码构建和真实 worker 输出，未替换此前的自包含开发发布包。
