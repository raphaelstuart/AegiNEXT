<p align="center">
  <img src="src/AegiNext.Desktop/Assets/AppIcon.png" width="112" alt="AegiNext icon" />
</p>

<h1 align="center">AegiNext</h1>

<p align="center">现代化、原生跨平台的字幕制作软件</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="docs/zh-cn/quick-start.md">快速开始</a> ·
  <a href="docs/zh-cn/README.md">中文文档</a> ·
  <a href="docs/en/README.md">English Docs</a>
</p>

AegiNext 致力于提供更易上手的字幕制作体验，兼容 Aegisub 文件标准，原生支持 macOS 与 Windows。开发目标是在 Aegisub 原作者停止维护后，接替旧版 Aegisub，延续字幕制作工作流。

## 功能

- **字幕制作**：多轨编辑、波形与语谱图打轴、ASS/SRT 导入导出。
- **样式与特效**：富文本、卡拉 OK、关键帧、运动路径、蒙版和可复用脚本。
- **现代工作台**：可停靠面板、明暗主题、双语界面与自定义快捷键。
- **保存与导出**：自动保存、备份恢复、独立进程视频压制与 HDR 导出。

## 快速开始

1. 打开应用并创建工程，通过 **文件 → 打开视频**载入媒体。
2. 选择轨道，按 **F8 / F9** 标记字幕起止时间，再输入文字。
3. 调整样式，按 **Cmd/Ctrl + S** 保存，通过**格式**导出字幕或在**压制**面板导出视频。

## 从源码运行

先安装仓库要求的 .NET SDK 和 PowerShell，以下命令均在仓库根目录执行。

### 安装依赖

macOS 使用 Homebrew，Windows 使用 Scoop。Windows 生成发布安装包还需要安装 NSIS：

```powershell
scoop bucket add extras
scoop install nsis
```

安装缺失的构建依赖并构建工作台：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -InstallDependencies
```

### 构建

后续构建执行：

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

### Release 打包

```powershell
pwsh -NoProfile -File ./release.ps1
```

在目标系统运行，macOS 生成 DMG，Windows 生成 NSIS 安装包；产物位于 `artifacts/releases/` 下的新目录。

环境配置、启动与调试见[构建指南](docs/zh-cn/building.md)，打包见[发布指南](docs/zh-cn/publishing.md)。

## 许可证

GPLv3。
