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

Style and effect Inspector inputs publish valid drafts immediately to the shared canvas and video composition state. The authoritative editor and Undo history change only on commit (lost focus, numeric Enter, or an explicit action). Invalid or incomplete text retains the last valid preview and remains editable; unrelated subtitle/export drafts do not block the local preview. Font search remains a draft until its existing confirmation boundary. Pending preview/focus callbacks reject changed targets, snapshots, and disposed hosts.

## Menus and native chrome

`AegiNEXT - Project name •` is shared by custom/native titles; the old project/settings row is removed. Registered roles distinguish main/floating/settings/auxiliary/modal windows. Only the main window projects a window menu. Every workbench window shares command context and a lasting native-menu root for macOS focus changes; root identity is not cleared. System menu is the default; Appearance persists and immediately switches mode. Top-level Layouts never gets a modified suffix.

Menus/buttons occupy interactive regions and real native button safe areas; remaining title space drags. Windows uses real DWM buttons with full native style/nonclient hooks and DwmDefWindowProc. macOS uses full decoration/client extension and actual NSWindow traffic-light measurements. See [windowing](workspace-windowing.md).

macOS platform probes use IMacOSTopLevelPlatformHandle.NSWindow. Rider flags the locked platform interface as unstable/CS0618; CLI builds historically had no warnings, and native probes exercised it. Reverify on Avalonia upgrades; do not replace with reflection or broad suppression.

Old complete shortcut arrays migrate customized/disabled bindings and append new commands. Unknown/duplicate/conflicting/incomplete old arrays reject. Each window gets one router; recording/text/popups retain local behavior. Language changes update builtins/menus/pages, preserving custom names.

## Localization

`AegiNext.Desktop.I18n.Localization` owns the loaded language catalog and current language snapshot. The application explicitly calls `Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"))` on the UI thread before loading application XAML, then applies the loaded preference before main-window XAML. `Get(string key)` reads the current snapshot on any thread; `Format(string key, params object[] arguments)` formats with `CurrentCulture`. `SetLanguage(string lang)` runs on the UI thread, accepts an installed `LanguageID` or `system`, updates existing text immediately, and rejects unknown IDs without changing the current language. The service does not save preferences. Dynamic ViewModels, menus and titles subscribe to `LanguageChanged` at their owning lifetime boundary and unsubscribe on close/disposal.

Static text uses the unprefixed `{Loc Key=...}` markup extension on `Text`, `Content`, `Header`, tooltip and accessibility properties. It returns a live observable binding without replacing the business `DataContext`. Translation `Tag` attributes and control-tree localization scans have been removed. For example:

```xml
<TextBlock Text="{Loc Key=Workbench.Codec}" />
<Button Content="{Loc Key=Workbench.Cancel}" />
<Expander Header="{Loc Key=Settings.Advanced}" />
<Button ToolTip.Tip="{Loc Key=Workbench.Save}">
  <common:IconText Text="{Loc Key=Workbench.Save}" IconKey="Save" />
</Button>
```

The last example declares `xmlns:common="using:AegiNext.Desktop.Controls.Common"` on its root. `IconText.Text` accepts the same localization binding; `IconKey` independently uses the existing `WorkbenchIcon` IDs/aliases, preserving the current icon mapping. Renaming a translation key does not change the icon. State-dependent text such as play/pause or shortcut recording remains a ViewModel binding whose getter calls `Localization.Get`.

Source files under `src/AegiNext.Desktop/I18n/Languages/` are UTF-8 JSON without a BOM and are copied automatically to `i18n/` in build, test and publish outputs. Each JSON root supplies nonempty `LanguageName`, a valid culture `LanguageID`, and a string dictionary `Strings`; keys keep their source prefix, such as `Workbench.Export` or `Settings.Export`. Settings populate their language choices from `KnownLanguages` plus the localized `system` option, display `LanguageName`, and select/save by ID rather than fixed indices. A valid saved ID whose package is missing remains in preferences while the application uses English.

Startup discovers the directory's first-level JSON files; editing or adding a language requires a restart, with no hot reload. `system` resolves the captured system culture by exact ID, parents, then candidates of the same language; Chinese/English prefer `zh-CN`/`en-US`, other candidates sort by ID, with final `en-US` fallback. A missing translation falls back to `en-US`, then the raw key. Invalid optional packs and conflicting ID groups are excluded with diagnostics; a valid unique `en-US` pack is required. Language changes preserve personal names, subtitle content and pending drafts. Automated tests and package checks remain separate from final native visual acceptance of icons, long translated labels and platform menus.

## Subtitle position, curves, and video editing

Timeline keyframe dragging changes only time along the horizontal axis. Vertical pointer movement preserves every scalar, vector, and color component and creates no edit when time stays unchanged. Keyframe values are entered through the Effects panel inputs, with live draft preview and one transaction on confirmation.

Any subtitle track can be deleted, including the last track. Empty tracks are removed immediately; populated tracks require confirmation showing the track name and subtitle count, with Cancel as the default action. Deletion removes the track's subtitles and corresponding layers, animations, and masks in one Undo transaction, preserving other tracks, graphics, and shared assets. A project may temporarily have no subtitle tracks and can still be saved and reopened. Add subtitle and F8 remain disabled until a track is added; subtitle import creates its own tracks, and graphics-only clipboard operations remain available.

Timeline subtitle, shape, and image clips support platform-modifier toggles and Shift range selection. Dragging the body of an already selected clip moves the whole selection horizontally, preserving each track, duration, and relative timing. The earliest clip stops the whole selection at zero. Moves may pass other subtitles; a final same-track overlap makes every moving clip red and restores the original positions on release. Snap uses the grabbed clip's start/end and excludes every moving member. Single subtitle moves retain cross-track drops; edge trim remains a single-clip edit. Clicking an expanded subtitle track's empty effects area selects that track.

With the timeline focused, Cmd/Ctrl+C copies its selection, Cmd/Ctrl+V pastes at the current pointer time and track, and Delete removes the selected batch. Copy freezes the selected reference track and source track order; Paste aligns that reference with the destination track and keeps the other subtitles' track spacing. Mapping beyond existing tracks rejects the entire paste. Keyboard paste resolves the pointer against the current viewport when executed; headers, the ruler, positions outside the timeline, and active drags have no target. The body context menu freezes its selection, time, and track when opened, and can also create a two-second subtitle on the clicked track. Copies retain content, relative timing, and composition order within the current project; final subtitle collisions reject the entire paste or creation. Move, paste, creation, and batch deletion each have one Undo transaction. Snap, Step, Spectrum, and Waveform are application-wide personal preferences, shared by windows and restored after project switches and restart; defaults are on, off, on, and on.

Waveforms use independent 48 kHz mono analysis. Zoom and display scaling select a power-of-two number of PCM samples per peak bucket; each physical pixel aggregates minimum and maximum peaks and receives one fill, preserving transients without accumulating opacity. The visible range is analyzed with half a viewport of padding on each side, capped at 16384 buckets. Workspace coalesces requests for 75 ms, permits one detail worker, and retains a 32 MiB peak cache with at most 256 entries. A separate overview supplies unloaded ranges. Playhead, color, and vertical scrolling updates reuse drawing geometry. Hiding cancels pending waveform work; media replacement and closing cancel and drain all analysis, and obsolete results cannot replace the latest view. Changing waveform resolution does not seek playback or edit the project. Real WAV integration covers exact sample boundaries; coarse container timestamps retain the native decoder's positioning precision (a 1 ms Matroska time base produced a measured ±0.333 ms local-seek offset).

Clip start and end boundaries span the timeline body below the ruler, using thin 1.5-DIP lines for selected clips and quieter gray lines for other clips. Transparent range fills cover the waveform/spectrum beneath the clip rows. Selected clips are highlighted; unselected clips and their text are subdued. Overlapping ranges are partitioned once with invalid-drag and selection priority, and coincident boundaries are drawn once. Previewed moves and trims update the boundaries without editing the project. Geometry is cached across playhead/hover refreshes. Settings → Colors exposes selected/unselected clip colors, start/end line colors, and selected/unselected range fills with RGBA opacity. These are shared personal preferences; old preferences retain theme-aware defaults, and custom colors remain exact across theme changes.

With the timeline or a selected subtitle list row focused, Q plays up to 500 ms before the primary subtitle, W up to 500 ms after it, E its first 500 ms (or the whole subtitle if shorter), and R the whole primary subtitle. The duration is configurable in Settings → Preview. These follow Aegisub's audio audition mappings; multiple selections do not change the primary target. Subtitle text and timing inputs retain their local input behavior, including IME composition. Audition seeks audio and video to the range start and plays them together; the timeline playhead, preview progress, and presented frames follow the audio clock. Space or the preview button pauses both at the current position. Completion stops at the range end; ordinary playback resumes there, unless that position is the media end, where it restarts from the media origin. Media boundaries clip unavailable audio, and empty ranges do nothing. A new audition, ordinary seek/play/pause, or media close replaces the previous range. Subtitle-details playback owns its own cancellation token, so delayed cleanup cannot stop a newer audition. All four commands are editable and clearable in Settings → Shortcuts; old shortcut sets are extended without replacing user bindings, and conflicting new defaults remain disabled.

Selecting or focusing a subtitle list row centers the primary clip in the timeline body at the current zoom, including keyboard navigation and floating timeline panels. The view starts at zero when centering would require negative time, and may show blank space after the project end. A clip wider than the viewport keeps its midpoint centered without zooming out. Selection during playback temporarily suspends playhead auto-scrolling; explicit playback, positioning, audition, or timing creation restores it. Centering is presentation state and creates no project edit or Undo transaction. Direct timeline selection and drag gestures preserve their viewport.

Explicit positioning uses normalized canvas Anchor, normalized actual-ink Pivot, and pixel Offset, top-left origin with X right/Y down. `(0.5,1)` anchor/pivot and `(0,-40)` offset put the text's bottom-center 40 px above canvas bottom. Natural text is never stretched. Automatic nine-position alignment remains when explicit positioning is off; enabling explicit position compensates measured ink so text does not jump.

ProjectSceneRenderer.GetLayerGeometry shares actual glyph bounds/positions and parent transforms with drawing/selection/drag. Space advance does not inflate nonempty ink bounds; blank subtitles have a logical editing box. Stroke/shadow/blur do not change pivot. SubtitlePositionDraft retains raw numeric text; SubtitlePositionEditor/VectorDraftInput are shared by Styles and template Settings, without Dock/session dependencies. `.aegistyles` exchanges position and fonts with hash/budget checks. Current projects use v5; v3/v4 files are migrated, while v1/v2 are rejected. Earlier v2 compatibility statements do not apply to project loading now.

The Effects position reset clears only position animation, displacement, and motion paths, preserving the subtitle Anchor/Pivot/Offset; the Styles automatic-position action restores alignment-based placement. The subtitle list supports the platform selection modifier and Shift ranges, keeps stable selected IDs across refresh, and merges the actual selected consecutive cues in one transaction. A single selected cue still merges with its next cue; the last cue disables that action. Incompatible effects or nonconsecutive selections are rejected without partially changing the document.

In a subtitle list content input, Enter commits valid workspace drafts and focuses the next row on the current track, including an empty row. At the last row, it creates an empty subtitle from that row's committed end to the playhead position captured on the key press, using the existing track/preset style rules, then focuses its content input. If the playhead is at or before the current end, creation is rejected with an explanation and the content input keeps focus. The default Shift+Enter command inserts a newline at the caret or replaces the selection through native text input, preserving text Undo; timing inputs keep their existing keyboard behavior. Both list commands can be rebound or disabled in Settings → Shortcuts, and clearing a binding prevents implicit Enter newline insertion. Holding Enter advances once per press, and IME candidate confirmation does not trigger navigation. Invalid drafts retain their raw text and focus their error field. Font preparation and panel reattachment do not steal a focus the user has moved elsewhere.

Expanded tracks continuously show **all clips' existing properties**, with vector components sharing one row and RGBA one row with independent Alpha geometry. Real-time marker projection is shared by rendering/hit/hover/gestures; coincident components combine without artificial horizontal offsets. Labels appear only on hover in a final overlay after clip cropping. Moving shifts clip/keyframe project time; trim keeps phase and inserts boundary values; stretch scales time. Content bounds are `[max(0, AnimationOffset), AnimationOffset + End - Start]`; endpoint editing does not change half-open playback.

Each animation property row has its own collapse triangle beside its title. A collapsed row is 40 DIP high and shows a horizontal line over each clip's visible interval, with keyframes at their original time positions. Vector and color components of one keyframe share a marker; mask nodes and transform endpoints retain their identities. Markers remain selectable and draggable in time, with values and interpolation preserved. Expanding restores the value curves. Clips on the same subtitle track share each property's collapse state; other subtitle tracks and scene layers have independent states. Collapsing and expanding the whole track or group preserves its inner property states.

Property collapse states are saved with the project, including manual Save, Save As, and autosave. This view change marks the project unsaved and participates in the close check, without adding content Undo or discarding Redo. Returning to the saved collapse state clears its unsaved flag when content is also unchanged. Collapse clicks preserve focused Inspector drafts and their last valid previews. New projects and older files without the optional view state open with all property rows expanded. The optional v5 field identifies collapsed rows by owner scope, owner GUID, and animation property; temporarily absent rows remain available after content Undo. Save captures content and view state together, and later changes during autosave remain unsaved. The existing whole-track/group collapse behavior is unchanged and is not persisted by this field.

Preview is the sole visual editing surface. Effects has no separate preview/layer list or general shape/image/group/mask tools. Preview renders background/scene/paths/selection/handles with the same visible rectangle. Each composed result retains its associated raw SDR background, preventing double subtitles or wrong-PTS background reuse. Presentation controls own/dispose frame pixels, not business models. Latest-only background/scene scheduling uses request/time/revision/quality identity and safe cancellation boundaries.

Export uses injected IWorkbenchExportService. Requests freeze project/parameters at start, path choice and encoding share awaitable nonreentrancy, cancellation rejects late progress, and close drains/releases once. Layout switches do not replace running jobs.

## Verification

Tests use real ProjectEditor and controlled media/dialog/export boundaries for atomic drafts/Undo/failure/seek/cancel/close. Headless contexts own session/models/host registry, locate real Views by Panel ID, send actual pointer/key input, and close every created host.

Historical isolated-build commands were:

```sh
dotnet restore AegiNext.sln --artifacts-path artifacts/workspace-build
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj --artifacts-path artifacts/workspace-build --no-restore
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --artifacts-path artifacts/workspace-build --no-restore
dotnet artifacts/workspace-build/bin/AegiNext.Desktop/release/aegi-next.dll --workspace-probe-auto --workspace-probe-report artifacts/verification/workspace-macos-release.json
```

Current RID-specific locks/intermediates follow [building](building.md). WorkspaceProbe uses temporary preferences, real main/floating/settings hosts, initial content, A→B→A identity, layout/preset independence, menu/native-button properties, and cleanup. DockSamples records real visual trees and attachment. It neither reads user projects nor writes their settings, and closes/removes its resources.

`--workspace-probe-source-profile DIR` copies only preference/style files into isolation, omits source layouts, and never writes back. It awaits library initialization then opens settings through the actual command. Reports record copied state/preset count/full exception and SettingsBindingExceptionCount. Internally handled model InvalidCastException still fails the probe. Historical page-count evidence remains tied to its original milestone; current Settings has five pages.

See [checkpoint index](README.md#implementation-evidence). Headless/native geometry is not evidence of traffic-light/DWM clicks, drag/tab/split/fullscreen/multimonitor/cross-DPI acceptance. Windows clicks already have separately dated evidence; Snap/DPI/physical device coverage stays explicitly outstanding.
