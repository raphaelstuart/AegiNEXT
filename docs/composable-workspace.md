# Composable workspace, MVVM, and window appearance

[English](composable-workspace.md) | [简体中文](zh-CN/composable-workspace.md)

The approved workspace/MVVM implementation covers the workbench and Settings; the independent HDR diagnostic retains its entry/lifetime. Later continuous-phase authorization superseded per-step pauses. Earlier milestones used six panels/project v2; the **current** baseline is seven panels and project v3. Historical reports are evidence for their stated version only.

## Space, features, and shared controls

| Directory | Ownership | Boundary |
|---|---|---|
| Workspace/ | Explicit composition root, sole WorkbenchSession, project/playback/analysis/export/style coordination | One ProjectEditor, controllers, selection, Undo; awaitable workflows |
| Layouts/ | Dock tree, tabs, splits, floating hosts, snapshots, personal presets | Seven fixed Views, stable IDs, draft and gesture callbacks; no project/task ownership |
| Panels/{Preview,Timeline,Subtitles,Styles,Effects,Export,Log}/ | Fixed feature View/ViewModel and local input adaptation | State/commands in models; Views wire only local controls and semantic time/ID/color/transform parameters |
| Controls/Common/ | Reusable chrome and common input | No main-window/Dock/project service access |
| Controls/Media/ | Frame presentation | Controls own/dispose bitmaps; business models do not |
| Controls/Editing/ | Timeline, canvas, font, numeric/vector/color drafts, DSL editor | Drawing/hit testing/gestures stay local; explicit events request edits |
| Settings/{Appearance,Colors,Shortcuts,Styles,Effects}/ | Five page models/drafts/validation/commands | Host composes pages, invalid source input survives |
| Menus/, Windowing/ | Shared commands, menu projection, input routing, native decoration/safety | Stable native menu roots; one command catalogue |

Locked versions: Avalonia **12.1.3**, CommunityToolkit.Mvvm **8.4.0**, Dock Avalonia/Model.Mvvm/Fluent **12.1.0.6**. Business models neither inherit Dock nor hold controls/windows. MainWindow locates only its own titlebar/dock host, never another panel's control.

Settings pages set an empty local DataContext **before** loading XAML, then receive the typed page model through compiled host bindings. x:DataType does not constrain runtime context; asserting only the final model misses transient initialization casts handled inside Avalonia. Startup tests/probes monitor FirstChanceException for these failures.

Fixed View/ViewModel instances survive docking/hiding/floating/preset switches. Thin Dock adapters wrap them. Templates install before the root layout; prebuilt panels and intermediate WorkbenchToolControl present immediately, avoiding deferred ToolChrome queues that blank cold-start content. Regression sets inherited delay to one day and still requires immediate initial attachment, without Loaded/timer/layout-switch workarounds. Timeline geometry follows real available height; compact tools never force an oversized drawing minimum.

## Layouts and personal presets

First launch offers read-only Standard/Timing/Effects/Export. Standard places Preview/properties above, Timeline centrally, Subtitles below, and Log as an inactive bottom tab. Drag titles/tabs to split/tab/float; View reopens and activates hidden panels.

The Layout menu title stays **Layouts**, without a modified suffix. Presets retain current checks/dirty state internally. Stable preset IDs drive switching; custom entries do not extend command enums. Save on a builtin routes to Save As; custom entries can save/rename/delete. Deleting the current name preserves actual space.

Personal `layouts.json` stores app-owned version 2 topology, ratios, tab order/active/hidden/focus, and floating position/scale. Version 1 six-panel layouts migrate strictly without discarding custom topology/names. Current layout coalesces autosave; named presets update only explicitly. Writes serialize through temporary file/flush/atomic replace, awaited before disposal. Restore constrains to current monitor work areas, retains valid negative coordinates, and moves orphaned floating windows onscreen. Corruption keeps a diagnostic copy and restores Standard.

Layouts are personal, survive project switches, and never alter project/Undo. Playback/selection/keyframes/viewport/export belong to shared session/panel state.

## Drafts and lifetime

Preset switching cancels gestures, validates pending subtitle/style/effect/export drafts, then commits valid project drafts in one transaction. Invalid source text stays visible, preset remains, and explicit validation can locate the field. Numeric drafts must retain raw text instead of falling back to an old NumericUpDown Value. Ordinary move/hide/floating-close only cancel gestures; container-detach blur must not accidentally submit.

Closing a floating window hides its panels and leaves session/tasks running. Main close performs one unsaved flow, awaits work/layout writes, closes registered settings/management/floating hosts, and releases media/analysis/export/preferences/presentation. Cancel or failed save retains session/input. Cleanup is idempotent with actual resource release once.

## Menus and native chrome

`AegiNEXT - Project name •` is shared by custom/native titles; the old project/settings row is removed. Registered roles distinguish main/floating/settings/auxiliary/modal windows. Only the main window projects a window menu. Every workbench window shares command context and a lasting native-menu root for macOS focus changes; root identity is not cleared. System menu is the default; Appearance persists and immediately switches mode. Top-level Layouts never gets a modified suffix.

Menus/buttons occupy interactive regions and real native button safe areas; remaining title space drags. Windows uses real DWM buttons with full native style/nonclient hooks and DwmDefWindowProc. macOS uses full decoration/client extension and actual NSWindow traffic-light measurements. See [windowing](workspace-windowing.md).

macOS platform probes use IMacOSTopLevelPlatformHandle.NSWindow. Rider flags the locked platform interface as unstable/CS0618; CLI builds historically had no warnings, and native probes exercised it. Reverify on Avalonia upgrades; do not replace with reflection or broad suppression.

Old complete shortcut arrays migrate customized/disabled bindings and append new commands. Unknown/duplicate/conflicting/incomplete old arrays reject. Each window gets one router; recording/text/popups retain local behavior. Language changes update builtins/menus/pages, preserving custom names.

## Subtitle position, curves, and video editing

Explicit positioning uses normalized canvas Anchor, normalized actual-ink Pivot, and pixel Offset, top-left origin with X right/Y down. `(0.5,1)` anchor/pivot and `(0,-40)` offset put the text's bottom-center 40 px above canvas bottom. Natural text is never stretched. Automatic nine-position alignment remains when explicit positioning is off; enabling explicit position compensates measured ink so text does not jump.

ProjectSceneRenderer.GetLayerGeometry shares actual glyph bounds/positions and parent transforms with drawing/selection/drag. Space advance does not inflate nonempty ink bounds; blank subtitles have a logical editing box. Stroke/shadow/blur do not change pivot. SubtitlePositionDraft retains raw numeric text; SubtitlePositionEditor/VectorDraftInput are shared by Styles and template Settings, without Dock/session dependencies. `.aegistyles` exchanges position and fonts with hash/budget checks. Current project is v3; earlier v2 compatibility statements do not apply to project loading now.

Expanded tracks continuously show **all clips' existing properties**, with vector components sharing one row and RGBA one row with independent Alpha geometry. Real-time marker projection is shared by rendering/hit/hover/gestures; coincident components combine without artificial horizontal offsets. Labels appear only on hover in a final overlay after clip cropping. Moving shifts clip/keyframe project time; trim keeps phase and inserts boundary values; stretch scales time. Content bounds are `[max(0, AnimationOffset), AnimationOffset + End - Start]`; endpoint editing does not change half-open playback.

Preview is the sole visual editing surface. Effects has no separate preview/layer list or general shape/image/group/mask tools. Preview renders background/scene/paths/selection/handles with the same visible rectangle. Each composed result retains its associated raw SDR background, preventing double subtitles or wrong-PTS background reuse. Presentation controls own/dispose frame pixels, not business models. Latest-only background/scene scheduling uses request/time/revision/quality identity and safe cancellation boundaries.

Export uses injected IWorkbenchExportService. Requests freeze project/parameters at start, path choice and encoding share awaitable nonreentrancy, cancellation rejects late progress, and close drains/releases once. Layout switches do not replace running jobs.

## Verification

Tests use real ProjectEditor and controlled media/dialog/export boundaries for atomic drafts/Undo/failure/seek/cancel/close. Headless contexts own session/models/host registry, locate real Views by Panel ID, send actual pointer/key input, and close every created host.

Historical isolated-build commands were:

```sh
dotnet restore AegiNext.sln --artifacts-path artifacts/workspace-build -p:RestoreLockedMode=true
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --artifacts-path artifacts/workspace-build --no-restore
dotnet artifacts/workspace-build/bin/AegiNext.Desktop/release/aegi-next.dll --workspace-probe-auto --workspace-probe-report artifacts/verification/workspace-macos-release.json
```

Current RID-specific locks/intermediates follow [building](building.md). WorkspaceProbe uses temporary preferences, real main/floating/settings hosts, initial content, A→B→A identity, layout/preset independence, menu/native-button properties, and cleanup. DockSamples records real visual trees and attachment. It neither reads user projects nor writes their settings, and closes/removes its resources.

`--workspace-probe-source-profile DIR` copies only preference/style files into isolation, omits source layouts, and never writes back. It awaits library initialization then opens settings through the actual command. Reports record copied state/preset count/full exception and SettingsBindingExceptionCount. Internally handled model InvalidCastException still fails the probe. Historical page-count evidence remains tied to its original milestone; current Settings has five pages.

See [checkpoint index](README.md#implementation-evidence). Headless/native geometry is not evidence of traffic-light/DWM clicks, drag/tab/split/fullscreen/multimonitor/cross-DPI acceptance. Windows clicks already have separately dated evidence; Snap/DPI/physical device coverage stays explicitly outstanding.
