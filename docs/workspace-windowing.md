# Workspace windowing foundations

[English](workspace-windowing.md) | [简体中文](zh-CN/workspace-windowing.md)

Dock/MVVM covers workbench/Settings, retaining the independent HDR window. This records original platform foundations now integrated into main, floating, and settings hosts. [Composable workspace](composable-workspace.md) describes ownership/presets/lifetime.

## Layers

| Location | Responsibility | Boundary |
|---|---|---|
| Controls/Common/WindowTitleBar | Title/menu, interaction exclusions, caption safety | No project/Dock/command catalogue/native API |
| Windowing/ | Platform policy/client geometry/buttons/Win32 messages/disposal | Window/titlebar only, no panel/project state |
| Diagnostics/WindowChromeProbe* | Independent three-host probe/report | No user project/preferences; not business-session acceptance |

Workspace owns the sole session/coordinators; Layouts space; Panels seven fixed features including Log; Common/Media/Editing reusable controls; Settings page models; Menus/Windowing commands/input/native policy. Business models hold no Dock/control/native events. Locks: Toolkit 8.4.0, Dock packages 12.1.0.6, Avalonia 12.1.3.

WorkbenchDockTemplateCatalog wraps actual ToolChrome. PART_WorkspaceFrame uses 10-DIP WorkbenchPanelCornerRadius, PreviewSurface/PreviewBorder, and matching clipping. Internal/panel borders do not duplicate it. Active/inactive titles share background with accent text/compact icons. ToolTabStrip filler draws no top line; items draw no normal/selected/hover border; selection uses accent text plus 14% accent background with unchanged padding. Floating panel and business content share corners. Native window decoration is a separate concern; [pixel/lifetime evidence](checkpoints/timeline-completion-polish.md).

## Title and client sizing

WindowTitleBar exposes Title/MenuContent/CaptionInsets. Real native geometry reserves sides. Menu/button rectangles are interactive; remaining title uses native drag semantics.

Avalonia macOS ChromeHitTest reads the directly hit Visual's decoration role, not ancestor roles. The whole transparent title background must be TitleBar; button safety/menu User. Marking just text/root does not make blanks draggable. Tests cover direct/ancestor hit, padding/menu gaps/empty title/left-right safe regions.

WindowChrome.Attach binds Window.Title and native adapter with WindowDecorations.Full; there are no imitated minimize/maximize/close buttons.

Resizable main/floating/settings hosts use Manual sizing. Unsaved/layout-name dialogs use fixed width and SizeToContent.Height including custom chrome; wrapped text grows/shrinks content. Adapter preserves policy; Windows never backfills manual restore size for autoheight. IWindowChrome.ResizeClient accepts finite positive client dimensions and centralizes native frame conversion. Min/max adjusts restore size; fullscreen defers. Dispose unregisters hooks/events and queued refreshes do not access disposed hosts.

### macOS

Full system decoration/client extension incorporates custom titlebar while retaining NSWindow traffic lights. standardWindowButton measures real safety. CGRect dispatch differs: arm64 objc_msgSend versus x64 objc_msgSend_stret.

System menu is default. Window mode projects only the main menu; floating/settings/auxiliary/modal hosts still register shared native command roots. Focus never clears/replaces exported roots. Preferences/Appearance apply mode immediately. Actual NSApplication.mainMenu checks are [separately recorded](README.md#implementation-evidence).

### Windows

Keep full native styles, disable Avalonia client extension, use public Win32Properties hook for WM_NCCALCSIZE/client size/edge resize, DWM frame extension, and DwmDefWindowProc-first native button/nonclient messages.

Autoheight after layout computes desired content/padding/border/min-max through the same native conversion, preserving SizeToContent. Physical-pixel comparisons and coalesced refresh prevent repeated size correction. No extra original-caption chin is added; changing text expands/shrinks. [Measured sizing](README.md#implementation-evidence).

DWMWA_CAPTION_BUTTON_BOUNDS reserves right safety. Transparent window composition/root drawing clipping avoids opaque client paint covering actual buttons. Maximize may return client hits; TITLEBARINFOEX supplies system rectangles/enabled state. DPI/frame refresh coalesces to Dispatcher; errors enter reports. x86 uses GetWindowLongW, x64 GetWindowLongPtrW.

WM_GETMINMAXINFO alters tracking size only, retaining OS MaxSize/MaxPosition for secondary-monitor work areas. WM_WINDOWPOSCHANGING stays with Avalonia; original path review used pinned source/Win32 docs and did not itself supply runtime evidence.

Interop/geometry tests on macOS do not prove Windows pixels/clicks. On 2026-10-05, Parallels Windows 11 real minimize/maximize/restore/canceled close/final close ran on all three hosts, with screenshots. Snap/cross-DPI/multiple monitors remain manual; [evidence scope](README.md#implementation-evidence).

## Independent probe

Three simulated hosts share window foundations, rather than claiming complete business workflow coverage. Auto closes them and exits; interactive main close closes others and writes a report.

```sh
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj \
  --configuration Release --artifacts-path artifacts/window-chrome-build \
  -p:RestoreLockedMode=true

dotnet artifacts/window-chrome-build/bin/AegiNext.Desktop/release/aegi-next.dll \
  --chrome-probe-auto \
  --chrome-probe-report artifacts/verification/window-chrome-auto.json

dotnet artifacts/window-chrome-build/bin/AegiNext.Desktop/release/aegi-next.dll \
  --chrome-probe \
  --chrome-probe-report artifacts/verification/window-chrome-interaction.json
```

Auto resizes/restores clients, switches menus, maximizes/restores, cancels one close then releases. Samples record client size/scaling/decorations/menu/buttons/errors. NativeInteractionStatus remains Requires manual verification: programmatic state changes are not native clicks/drag/visual evidence.

Chrome and HDR probes are mutually exclusive; normal entry is the workbench and HDR behavior remains independent.

## Manual acceptance and references

macOS checks traffic lights, blank-title drag, menu nondrag, both modes, min/restore/fullscreen/close cancel, narrow layouts. Windows checks actual button draw/hover/click/Snap, title/eight-way resize/min size/max restore/taskbar/work areas/DPI and programmatic restore sizing. Complex visuals need user screenshots/device runs; record them separately from automation.

- [Microsoft custom frames](https://learn.microsoft.com/en-us/windows/win32/dwm/customframe)
- [DwmDefWindowProc](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmdefwindowproc)
- [GetWindowLongPtr](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowlongptrw)
- [MINMAXINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-minmaxinfo)
- [Pinned Avalonia macOS WindowImpl](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/native/Avalonia.Native/src/OSX/WindowImpl.mm)
