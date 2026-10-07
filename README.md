<p align="center">
  <img src="src/AegiNext.Desktop/Assets/AppIcon.png" width="112" alt="AegiNext icon" />
</p>

<h1 align="center">AegiNext</h1>

<p align="center">Modern, native cross-platform subtitle creation</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="https://raphaelstuart.github.io/Aegisub-NEXT/">Documentation website</a> ·
  <a href="docs/en/quick-start.md">Quickstart</a> ·
  <a href="docs/en/README.md">English Docs</a> ·
  <a href="docs/zh-cn/README.md">中文文档</a>
</p>

AegiNext makes subtitle creation easier to learn, supports Aegisub file standards, and runs natively on macOS and Windows. Its goal is to succeed the original Aegisub after its author stopped maintaining it, carrying the subtitle creation workflow forward.

![](docs/assets/screenshot.jpg)

## Features

- **Subtitle creation**: multiple tracks, waveform and spectrogram timing, and ASS/SRT import and export.
- **Styles and effects**: rich text, karaoke, keyframes, motion paths, masks, and reusable scripts.
- **Modern workspace**: dockable panels, light and dark themes, a bilingual interface, and custom shortcuts.
- **Saving and export**: autosave, backup recovery, video encoding in an independent process, and HDR export.

## Quickstart

1. Open the app, create a project, and load media with **File → Open Video**.
2. Select a track, press **F8 / F9** to mark subtitle start and end, then enter the text.
3. Adjust its style, save with **Cmd/Ctrl + S**, and export subtitles through **Format** or video through **Export**.

## Aegisub compatibility

The following classic Aegisub audition shortcuts and mouse timing controls are supported. Check **Settings → Shortcuts** for your current key bindings.

| Default key / mouse | Action |
|---|---|
| Q | Audition a short range before the subtitle starts |
| W | Audition a short range after the subtitle ends |
| E | Audition a short range from the subtitle's start |
| R | Audition the whole subtitle |
| Left click in the timeline body | Set the primary selected subtitle's start at the clicked time |
| Right click in the timeline body | Set the primary selected subtitle's end at the clicked time |

Q/W/E/R work with Timeline or a subtitle row focused, outside text editing. Short ranges default to **500 ms**, configurable in **Settings → Preview**.

For mouse timing, select a subtitle and enable **Classic Aegisub timing** using the mouse icon at the timeline's bottom left; it is off by default. While enabled, left and right clicks in the body edit the primary selected subtitle without changing selection. Turn it off to restore normal selection, dragging, and clip context menus. See the [shortcut guide](docs/en/quick-start.md#classic-aegisub-controls).

## Subtitle and project formats

| Format | Preserved content | Access |
|---|---|---|
| ASS (`.ass`) | Base/local styles, karaoke, static positioning, and supported masks | Format → Aegisub → Import / Export |
| SRT (`.srt`) | Subtitle text and start/end times | Format → SRT → Import / Export |
| AegiNext project (`.aeginext`) | Project data, tracks, styles, effects, and media references | File → Open Project / Save / Save As |

Imports create independent tracks; overlapping cues keep their times on additional tracks. Exports include all subtitle tracks. Save an `.aeginext` project to continue editing.

Unsupported ASS tags or animation require confirmation before import conversion; `\move` is not converted into a project motion effect. SRT export reports loss of rich styling, karaoke, masks, and animation. See [subtitle import and export](docs/en/subtitle-editing.md#import-and-export) for the full supported scope.

## Run from source

Install the .NET SDK and PowerShell required by the repository. Run the following commands from its root.

### Install dependencies

Dependency installation uses Homebrew on macOS and Scoop on Windows. For Windows release packaging, also install NSIS:

```powershell
scoop bucket add extras
scoop install nsis
```

Install missing build dependencies and build the workbench:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -InstallDependencies
```

### Build

For subsequent builds:

```powershell
pwsh -NoProfile -File ./build.ps1 -Target Workbench -Configuration Release
```

### Release

```powershell
pwsh -NoProfile -File ./release.ps1
```

Run on the target OS to create a macOS DMG or Windows NSIS installer. Packages are written to a new directory under `artifacts/releases/`.

See [Building](docs/en/building.md) for setup, launching, and debugging, or [Publishing](docs/en/publishing.md) for packaging.

## License

GPLv3.
