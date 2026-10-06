# AegiNext 架构与实施边界

[English](../architecture.md) | [简体中文](architecture.md)

## 当前状态与产品范围

AegiNext 使用 .NET 10、C# 与 Avalonia，根命名空间为 `AegiNext`，首版面向 macOS 和 Windows；Linux 留待后续。当前已落位时间与项目模型、字幕编辑及撤销重做、TXT/SRT/自有项目存储、共享 F16 场景渲染、视频解码与 SDR 预览、SDL3 音频播放、波形／时频分析，以及独立压制 worker。首版仍在集成与界面验收阶段；模块测试通过不等于全部界面操作、Windows 实机或最低系统版本已经验收。

产品围绕当前字幕项目：逐行编辑时间、内容和样式，在视频与音频时间轴上打轴，编辑字幕的关键帧、运动路径、Clip 蒙版及脚本特效，然后压制成片或导出字幕。普通边缘拖动执行裁剪，Ctrl 拖动片段边缘执行显式时间拉伸；ASS 导入／导出将已支持标签适配到工程模型，自有 JSON 项目保存更完整的场景及动画状态。

用户已调整首版优先级：普通 SDR 映射预览先行，HDR 成片必须保持高精度；原生 HDR 显示不再阻塞主编辑器。已有 macOS HDR probe 保留为可选诊断，Windows 原生 HDR 预览延期。主编辑器和导出都不依赖该 probe 的显示窗口。

## 模块与依赖方向

| 位置 | 职责 |
| --- | --- |
| `src/AegiNext.Core` | 纯 C# 的有理时间、媒体事实、版本化项目、场景树和编辑求值；`PreparedProjectScene` 验证不可变快照并缓存索引，`SceneEvaluator` 求值关键帧、路径及局部时间。 |
| `src/AegiNext.Application` | 编辑事务与撤销重做、SRT/TXT 交换、项目保存／加载、资源导入与另存为重定位；项目内媒体使用相对引用，项目外媒体使用外部引用，字体／图片使用托管相对路径和可选 SHA-256。 |
| `src/AegiNext.Rendering` | SkiaSharp/HarfBuzz 的线性 F16 表面与共享项目渲染器：文字、卡拉 OK、图形、图片、变换、蒙版、分组、模糊和混合；提供 SDR 预览叠层合成。 |
| `src/AegiNext.Media` | FFprobe 正式进程适配器、原生解码／音频／导出 C ABI 绑定、精确定位、播放调度、CPU SDR 转换、音频分析、独立 worker 启动、进度／取消与输出提交。 |
| `src/AegiNext.Desktop` | Avalonia 工作台、字幕与效果编辑、视频／音频控制器、时间轴、主题和语言；调用应用服务，不将窗口或 ViewModel 传入 worker。 |
| `src/AegiNext.ExportWorker` | 独立无窗口进程；读取项目快照和明确的资源目录，复用 Rendering，驱动原生压制并复制音轨或 AAC 编码，成功后交还临时成片。 |
| `native/decoder` | 固定 FFmpeg 的软件解码、独立 AVFrame 所有权、seek 与帧事实；libswscale CPU CMS 派生 SDR 预览。 |
| `native/audio` | 固定 FFmpeg 音频解码／重采样与 SDL3 输出；保留媒体时间并向播放时钟提供消费进度。 |
| `native/export` | 独立 CPU 高精度压制库；FFmpeg 解码、编码域 YUV 重采样、官方 EOTF／矩阵转换、线性合成、H.264／HEVC 编码。**当前导出不依赖 libplacebo。** |
| `native/` 的 macOS HDR 模块 | Objective-C++、libplacebo 与 MoltenVK 的可选 FP16 EDR 诊断；与主编辑器及压制库分离。 |
| `Tests/` 与各 `native/*/Tests` | 按模块验证领域规则、资源、渲染数值、进程／句柄生命周期、真实媒体与成片；界面交互、音频设备和显示器效果另行记录。 |

依赖向内收敛：Desktop → Application / Media / Rendering → Core；ExportWorker → Application / Media / Rendering → Core。Media 可复用 Rendering，Rendering 只依赖 Core 与 Skia/HarfBuzz；Core 不引用 UI、媒体原生库或进程 API。原生实现由 Media 适配，worker 不依赖 Desktop，也不截取预览画面生成成片。

## 时间、项目与资源契约

`Core/Timing` 的 `MediaTime` 保存规范化有理秒，分子／分母为 `long`，分母严格为正；运算使用 `BigInteger` 中间值，约分后仍越界即抛出异常。`default(MediaTime)` 为 `0/1`。时基、时间戳和区间采用带验证的 `sealed record`；缺失 PTS 由适配器表达为 null，不能用零或猜测帧号补齐。

`MediaTimestamp` 的相等性比较整数 PTS 与时基表示；判断同一时刻必须比较 `ToMediaTime()`。例如容器将 `1/15360` 重标度成 `1/61440` 可以保持所有有理呈现时间不变。帧率不能替代 PTS。区间为半开 `[Start, End)`，支持重叠；项目原点固定零，`projectTime = mediaTime - MediaOrigin`。seek 以选定流绝对时基 backward 定位后精确 preroll，不自动归零或改变速度。

图层时间位于项目绝对时间轴；关键帧、路径和卡拉 OK 位于图层内容时间，`LocalTime = time - Start + AnimationOffset`。裁剪改变可见边界并补偿 offset，保留原动画速度；显式拉伸按有理比例修改局部时间。子层变换相对父组，普通字幕与同 ID 的字幕层一一对应。

`LayerAnimationTiming` 限制关键帧到片段可见内容时间，裁剪时插入边界值并保留原插值曲线子区间。项目当前写入版本为 **v5**，继续保存稳定字幕轨道、唯一 TrackId、自然字形位置与完整 Vector／颜色动画。读取 v3／v4 时迁移旧普通轨道，并兼容图层与预设中缺失或为 null 的旧蒙版，删除预设的空蒙版字段。任一对象包含非空旧局部蒙版时，报告对象位置及标识并拒绝升级，原文件保持不变。字幕样式模板仍使用既有版本，v1／v2 项目明确不支持。同轨 Clip 不允许重叠，轨道只组织字幕，不改变合成顺序。

`SubtitleTrack` 可保存 `DefaultStyle` 与来源预设 ID／名称。个人样式库用于选择，项目内快照用于后续创建，不持有个人库对象引用。`SubtitleStylePresetService.PrepareAsync` 先准备或复用字体资源，即使项目没有字幕也能执行；修改非空轨道预设时先通过 Workspace 询问是否更新现有 Clip：是更新，否仅保存后续默认值，取消不改变。Application 将所需资源、默认值及可选的现有字幕修改合为一次 Undo。轨道保存 AutoApplyStyle（缺省 true）；关闭时仍保留快照，后续创建使用样式面板当前选中的预设。Timeline 面板按稳定 Track ID 和 Preset ID 请求 Workspace 协调，不读取样式面板控件。手动新增、打轴和指定轨道导入继承默认值；拆分与跨轨移动保留原 Clip 样式。

`SubtitleLine.KaraokeStyle` 是单份 `KaraokeHighlightStyle` 视觉快照，保存填充、描边及阴影，不复制到各字素，也不改变字体、字号和排版几何。缺失快照时保留 `KaraokeSegment.HighlightColor` 行为。已有逐字片段换预设时保留内容时间；拆分继承有片段侧的快照，合并两侧已有片段时验证视觉兼容，比较不包含来源 ID／名称。新增轨道与高亮快照字段在 v3 读取边界允许缺失，未知字段和重复键继续拒绝。

`LayerTransform` 只持久化 Position / Scale / Pivot 三个 double 精度 `ScenePoint` 和 Rotation。`AnimationTrack.Target` 使用 `AnimationTrackTarget(Property, NodeId?)` 作为求值、编辑、脚本和时间线选择的完整身份；v3／v4 轨道的旧 `property` 字段迁移为 NodeId 为 null 的 `target`，v5 只接受目标表示。只有节点位置与控制柄目标携带 NodeId。共享 metadata 定义维度、分量和范围。`AnimationValue` 标量使用 number，二维值使用 `{x,y}`，颜色使用 `{red,green,blue,alpha}`；项目存储与媒体 worker 使用同一 Core JSON converter。旧 v3 分量按时间并集迁移，并保留每轴插值与裁剪相位；新数据拒绝继续写入分量轨道。所有编辑使用同一不可变快照与 Undo 栈。

轨道在普通关键帧和有序变换程序之间互斥。后者保存 InitialValue、稳定操作 ID、内容时间起止、目标值、非负 accel 和源顺序；求值依次将当前值插向各操作目标，保留重叠 ASS 变换。编辑禁止隐式转换两种表示。POWER 关键帧使用正指数，裁剪保留原曲线子区间；有序程序裁剪／拆分保留内容时钟，拉伸同步缩放操作时间。普通属性沿用原轨道预算，节点目标支持蒙版的 10,000 节点预算。

`ProjectLayer.Mask` 统一为 `ClipMask`，仅允许引用有效字幕的 SUBTITLE 图层持有。矩形保存工程坐标两角，自由路径保存具有稳定轮廓／节点 ID 的有序闭合贝塞尔轮廓；节点位置使用工程坐标，控制柄保存相对节点的偏移。蒙版拥有独立平移、缩放、旋转与固定工程坐标轴心，不继承字幕或父层变换。效果预设仅携带动画轨道，不复制蒙版几何。节点形变轨道锁定轮廓归属、顺序、数量和闭合状态；清除节点动画后解锁，整体变换仍保留。清除蒙版会在一次事务中删除全部蒙版轨道。

Desktop 的 ClipMaskEditingCoordinator 协调选择、草稿和提交。画布控件只持有局部指针手势与未闭合轮廓，闭合后一次提交；手势冻结 Clip 与内容时间，捕获取消、切换选择、Undo 或关闭时取消。数值字段有效输入立即预览，Enter／失焦提交一次，非法原文保留，Esc 只恢复当前字段。时间线节点行限定当前 Clip 与已选节点，拖动只改变时间。ASS 导入与高级文本编辑原子携带文字、蒙版、轨道与内容偏移；`.aegifx` 从 1 开始的轮廓／节点选择器在编译时解析为稳定节点 ID。

字幕特效 DSL 的纯文本语法、预算和精确时间编译位于 `Core/Effects/`，事务应用在 Application，个人模板库在 `Application/Presets/`，会话协调在 Desktop/Workspace，编辑器在 Controls/Editing，管理页面在 Settings/Effects。脚本固定段优先，自由段分配剩余时间，短 Clip 策略由源显式声明。七个内置脚本与用户模板同路径编译，不复制另一 Clip 的绝对关键帧长度。

项目文件保存不可变快照，每次保存和另存为按目标项目目录重新选择视频的相对或外部引用，不复制视频；同目录保存保留撤销／重做及保存点身份，不增加保存事务；另存为仅复制托管小资源。保存与资源复制校验完成后才提交，重定位验证已有资源哈希，不悄悄接受被替换的字体／图片。详细模型与行为以 Core、Application 的实现和测试为准。

## 共享渲染与 SDR 预览

字幕／图形工作表面是扩展线性 sRGB 原色、预乘 RGBA F16，RGB 可超出 `[0,1]`，参考白由项目显式提供，默认 203 nit。alpha 是覆盖率，不参与亮度标度转换。显式 F16 离屏层负责分组；模糊使用三次盒式高斯近似保留预乘高亮，加法混合使用扩展线性值，避免 Skia 部分默认离屏／滤镜路径的 SDR 钳制。字体和图片由项目资源解析器加载；预览与导出调用同一场景求值和渲染器。

字幕 `Position` 将画布 Anchor、实际字形 Pivot 和像素 Offset 组合为父级局部位置；未指定时使用九宫格自动排版，水平对齐按真实字形边界计算，居中偏移精确为零。`ProjectLayerGeometry` 同时提供实际字形边界、轴心及父级／世界变换，供渲染和编辑画布共同使用。纯业务模型不依赖 Skia；桌面会话通过独立位置解析器读取排版结果，呈现控件拥有位图。位置模板与字体样式使用同一 `.aegistyles` 文件交换，Anchor／Pivot／Offset 使用共享 `VectorDraftInput` 呈现，控件不持有项目或事务。

预设逐字高亮和原样式共用同一塑形结果。渲染按目标时间建立高亮裁剪区域，区域外绘制原样式、区域内绘制高亮填充／描边／阴影，避免在高亮下保留原样式的描边和阴影。原始默认逐字变色仍使用旧颜色覆盖路径。预览与独立压制 worker 使用同一模型和渲染函数。

预览背景从原始解码帧经固定 libswscale CPU CMS，以明确色彩政策映射为不透明 sRGB BGRA8，桌面默认适配 960×540，可选 1280×720 或 1920×1080；小视频不放大，交互最高 540p，松手恢复所选画质；未知色彩不猜测。背景再解码到线性显示表面，与项目叠层进行线性合成，最后编码回 sRGB。显示表面的名义参考白固定 203 nit；叠层 RGB 按 `project.ReferenceWhiteNits / 203` 重标，alpha 和视频背景不随项目参考白改变。静态场景使用精确状态比较缓存；修改快照、动画、卡拉 OK 和半开区间变化会使缓存失效。

SDR 预览是显示派生，不是 HDR 成片的像素参考；背景 tone mapping 与最终 HDR 合成所处阶段不同。预览 BGRA8、屏幕截图和显示器映射均不能作为压制输入。文字当前按单方向段落塑形并做整段字体回退，完整混合双向布局与逐 run 字体回退仍是后续边界。详见 [渲染契约](rendering.md)和 [SDR 预览](video-preview.md)。

## 实际离线压制管线

`Media/Encoding` 启动邻接的 ExportWorker，并传入项目快照、资源目录和输出参数；worker 在私有临时目录工作。视频使用独立 `native/export` 软件管线：

1. FFmpeg 解码原始整数 YUV，保留绝对 PTS、可见裁剪、位深与帧级色彩事实。libswscale 只在**同一编码域**重采样为 YUV444P16，不经过 SDR、BGRA8 或会钳制高亮的动态 CMS 浮点中间路线。
2. 有字幕覆盖的像素通过 FFmpeg 官方亮度系数矩阵与 EOTF 进入 double 精度绝对线性亮度。PQ 使用绝对 ST 2084 标度；HLG 明确采用 1000 nit、黑位 0 的参考显示条件及官方 OOTF；SDR 参考白为 203 nit。PQ 重建过冲仅约束到其有效编码域，避免负值进入幂函数；不将 HDR 钳制到 SDR 白。
3. 将共享渲染器的 F16 预乘线性 sRGB 层转入源原色，按项目参考白换算亮度后执行 source-over；alpha 保持原意。透明区域在该合成阶段直接保留编码域像素；最终编码仍受色度重采样、位深量化和所选 CRF 影响。
4. 使用官方逆 EOTF 回到源编码域，再重采样为 H.264 8 位或 HEVC 10 位。Auto 选择 SDR/libx264、HDR/libx265；固定 preset、CRF，CRF 0 的 x265 明确启用 lossless。没有经过 SDR 的背景不会仅靠修改标签伪装成 HDR。
5. 用 NUT 中间容器保留细粒度 PTS，再通过固定 FFmpeg 复用视频和选定音轨；音频可复制、AAC 编码或排除。验证输出后，父进程才将成片原子移动到目标，禁止覆盖旧文件；失败／取消清理本次临时目录。

GPU 选项只控制视频编码，要求真正初始化硬件且禁止静默回退；HDR 硬件输出明确拒绝，仍使用软件 HEVC。

当前输出尺寸必须等于视频可见裁剪尺寸，非方像素 SAR 明确拒绝；不静默缩放字幕画布。支持显式描述的不透明整数 YUV，要求已知且严格递增的呈现时间；负 PTS 仍受 NUT 能力限制，失败时不自动归零。旋转、立体、ICC、交错、损坏帧、动态格式／色彩变化及额外 HDR 处理策略尚不支持。

PQ 完整且稳定的 mastering display metadata 保留；部分或中途变化的 mastering 明确拒绝。旧 MaxCLL/MaxFALL 在合成后可能失效，不沿用为成片事实，输出未知值 0/0。HLG 使用上述固定显示条件。Dolby Vision、HDR10+、HDR Vivid 等动态元数据保真不属于首版支持范围。

## 原生 HDR 诊断、构建与验证边界

macOS 可选诊断采用 Vulkan/MoltenVK、FP16 Linear Display P3 和 CAMetalLayer EDR。上传 RGB 按 `sourceWhiteNits / 203` 重标，libplacebo 根据屏幕 headroom 映射；此处 203 是名义白，不能宣称屏幕实测亮度。交换链属性与 GPU 离屏回读单独验证。Windows D3D11/scRGB 原生 HDR 预览延期，详见 [原生 HDR 说明](native-hdr.md)。

固定工具链为 FFmpeg 9.0.2 及 manifest 中的开发库版本，SkiaSharp/HarfBuzz 版本与 Avalonia 依赖统一；可选 HDR 模块另锁 libplacebo、MoltenVK 和 Vulkan-Headers。构建入口区分 Managed、Decoder、Audio、Export 与完整 Workbench，见 [构建说明](building.md)。

已在本机完成原生颜色／ABI 测试、SDR/PQ/HLG 真实 worker 成片、HDR 高亮和半透明像素、静态 metadata、VFR 有理时间、音轨内容哈希、取消及不覆盖旧输出验证；亦完成 SRT→保存／加载→另存为→publish worker 的中文字幕成片链路。真实音频设备、界面和播放性能有独立日志，不能用这些结果推定 Windows 已实测。

当前 `publish.ps1` 生成固定版本 0.1.0 的平台自包含开发包，包含 .NET、worker、FFmpeg／FFprobe、自有媒体库、Skia／HarfBuzz 和完整非系统 native 闭包、哈希与第三方许可。macOS 加载路径重写并 ad-hoc 签名，最低系统版本依据实际依赖生成；本机包要求 macOS 27。Windows 显式 win-x64，主程序、worker 及 native 均校验为 x64，已在 Parallels ARM64 Windows 中通过系统模拟运行。发行公证、安装器和 Windows 代码签名仍不属于开发包范围。见 [平台发布](publishing.md) 和分开记录的真实运行证据。
