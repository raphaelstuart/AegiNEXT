# 特效脚本

[English](../en/effect-dsl.md) · [简体中文](effect-dsl.md) · [全部指南](README.md)

## 应用与编辑

1. 选择字幕，在**特效**中选择预设并点击**应用预设**。
2. 打开**设置 → 特效脚本**管理模板；内置模板只读，**另存为**创建个人副本。
3. 编辑后**验证**，再保存或导出 UTF-8 `.aegifx`；诊断提供行/列，**定位错误**移动光标。

输入时或 Cmd/Ctrl + Space 打开补全，方向键选择，Enter/Tab 插入，Esc 关闭。模板属于个人设置，应用时按当前 Clip 时长重新编译，并形成一条 Undo。

## 组合应用预设

可以连续应用多个预设。每个预设只覆盖它在某个时间段内明确声明的完整属性目标；没有声明该目标的区间保留已有动画及其插值曲线。没有已有轨道时，空段沿用脚本的基础值或前值。段名 `stay`、`hold` 没有特殊含义，`flex` 段也可以包含实际动画。

例如对 4 s Clip 先应用内置淡入，再应用内置淡出：前 300 ms 的淡入保留，中间动画保留，最后 300 ms 加入淡出。反向应用也得到相同结果。一次批量应用只有一条 Undo；保存并重新打开后仍能继续组合。

空停留段没有声明任何属性：

```text
segment stay flex 1
end
```

下面的停留段则明确要求将透明度固定为基础值，因此会覆盖已有透明度动画：

```text
segment stay flex 1
    at 0 opacity base hold
    at 1 opacity base
end
```

`hold` 是关键帧插值，不表示忽略该区间；`base` 始终读取静态基础属性或字幕样式，不读取旧动画在接合点的值。接合点两侧的值必须相等，否则整次应用失败，工程与 Undo/Redo 保持不变。可调整预设端点，或先清除对应轨道。

多个独立预设各自按 Clip 时长分配时间，不共同压缩固定段。两个 300 ms 进出场在 600 ms Clip 中可以直接接合；在 400 ms Clip 中会重叠，内置淡入接淡出因接合值不同而拒绝。在不超过 300 ms 的 Clip 中，每个预设都覆盖全片，后应用的预设完整替换前者。若希望短 Clip 同时保留进出场并共同压缩，请使用包含两段的单个预设，例如内置淡入淡出。

## 第一个脚本

```text
effect "fade-in-out" version 1
short-clip compress

segment enter fixed 300ms
    at 0 opacity 0 ease-out
    at 1 opacity base
end

segment stay flex 1
    at 0 opacity base hold
    at 1 opacity base
end

segment exit fixed 300ms
    at 0 opacity base ease-in
    at 1 opacity 0
end
```

`fixed` 保留进出场时长，`flex` 按权重分配剩余时间；至少需要一个弹性段。段内位置为 0–1，声明的每个属性必须覆盖两端且位置递增。

`short-clip compress` 在时长不足时按比例压缩固定段，`reject` 拒绝应用。上例 5 s Clip 停留 4.4 s，400 ms Clip 压缩为进出场各 200 ms；相邻共享端点必须相等，包括时长为零的弹性段。

## 属性与值

| 属性 | 值 |
|---|---|
| `position` | 像素向量：`base`、`(100, 20)`、`offset(-250, 0)` |
| `scale` | 向量：`base`、`(1, 1)`、`factor(0.2, 0.2)` |
| `rotation` | 角度：`0`、`base`、`offset(15)` |
| `opacity` | 0–1 或 `base` |
| `fill`、`stroke` | 线性 `rgba(r, g, b, a)` 或 `base` |
| `blur` | 0–512 像素或 `base` |
| `path-progress` | 显式 0–1 |
| `mask-rectangle-top-left`、`mask-rectangle-bottom-right` | 工程坐标的矩形角向量 |
| `mask-position`、`mask-scale`、`mask-rotation` | 独立蒙版变换 |
| `mask-node(c,n).position`、`.in-handle`、`.out-handle` | 已有节点位置 / 相对控制柄向量 |

`base` 读取应用前的目标值，`offset` 相加，`factor` 相乘。颜色采用非预乘线性 RGB，可表达 HDR，alpha 为 0–1；界面 HEX 使用 sRGB。`#` 表示注释，脚本不使用 HEX 字面量。

插值支持 `hold`、`linear`、`ease-in`、`ease-out`、`ease-in-out` 和 `power(正指数)`。当前点控制到下一点的插值，默认线性。

## 蒙版与校验

先创建几何再应用蒙版脚本。节点选择器从 1 开始，并解析为稳定 ID；节点/控制柄动画锁定拓扑，添加、删除或重排节点前清除对应轨道。脚本按完整目标组合声明区间，保留其他轨道，不创建几何。用脚本关键帧替换有序 ASS 变换前须先清除该目标。

ID/段名使用小写 ASCII 字母、数字、`.` 和 `-`，以字母开头，最多 64 字符。上限为 128 段、4,096 个点、262,144 字符、固定时长 24 h；弹性权重为 (0, 1,000]。无效输入或端点冲突不改变工程和 Undo。

## 示例

- [内置淡入淡出](../../src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx)
- [滑入/弹出](../en/examples/effects/slide-pop.aegifx)
- [线性颜色循环](../en/examples/effects/color-cycle.aegifx)
- [蒙版滑动](../en/examples/effects/mask-slide.aegifx)
- [蒙版形变](../en/examples/effects/mask-morph.aegifx)

解析/编译入口为 `src/AegiNext.Core/Effects/`。辅助编写可使用仓库[特效 DSL Skill](../../.agents/skills/aeginext-effect-dsl/SKILL.md)。
