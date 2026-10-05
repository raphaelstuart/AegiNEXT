# Workspace layout boundary

[English](layouts.md) | [简体中文](zh-CN/layouts.md)

`WorkbenchLayoutController` owns workspace **space**, not project, controllers, Undo, business models, or export jobs.

- WorkbenchPanelIds defines seven stable IDs. The composition root supplies seven fixed Views; WorkbenchDockPanel connects only ID/title/View. Log history is not layout data.
- WorkbenchDockFactory / WorkbenchDockSnapshotCodec are thin adapters; business interfaces do not expose Dock.
- WorkbenchDockPanelTemplate returns the supplied View. Preset/dock changes never recreate Views/models/session.
- Root/splits/tab groups/panel adapters implement IDeferredContentPresentation with immediate presentation. Existing controls attach without frame queues or timers before input.
- WorkbenchToolControl also presents immediately. Space templates retain Dock tracking/title/menu/tab/drag behavior without requeuing ready controls in ToolChrome.
- WorkbenchDockTemplateCatalog installs before Host receives layout; root/split/tab templates exist on first window construction, avoiding text placeholders.
- Common/Editing controls never reference Layouts. Space actions cancel gestures; only preset switching invokes the injected joint draft transaction.

## Host integration

Constructor inputs: main Window, stable-ID→Control map, personal settings directory, `Func<bool>` draft transaction, gesture-cancel Action, new-window registration Action. Place Host in the main body. Registration provides shared chrome/menu/input, not a second command catalogue.

FloatingWindowTitleChanged exposes the active panel title for a host coordinator to combine with project title. Floating hosts inherit Dock HostWindow for input but use the Avalonia Window style key, preserving native buttons rather than Dock replacement window chrome.

After approved main close, await FlushAsync before Dispose. Disposal closes floating/management windows and unregisters timers/observers. Floating close hides contained panels without disposing the business session. Reopen prefers the still-existing prior tab group; otherwise use main space.

## Persistence

Personal layouts.json is app-owned version 2, never serialized Dock objects. It records split ratio/orientation, tab order/active, focus, hidden IDs, and floating physical position/logical size/scaling.

Version 1 current and personal presets migrate only after strict six-panel validation. Preserve topology/active/floating/name; add Log hidden. Exact old builtins upgrade to the new corresponding topology with inactive Log. Normal atomic persistence writes v2 during autosave/close.

A UI coalescing timer schedules serialized writes using same-directory temp/flush/replace; close awaits the last. Current layout and named presets are separate fields. Builtins are read-only; Save As creates editable save/rename/delete entries.

Restore validates version/panel completeness/duplicates/depth/ratios/active tabs/window dimensions and file size. Corruption preserves a diagnostic copy and uses Standard. Work-area/scaling constraints keep orphaned windows visible and permit negative coordinates for monitors left of primary.

## Tests and acceptance

Tests/AegiNext.Desktop.Tests/Layouts covers topology, four read-only presets, current/named isolation, queued atomic writes/failures, corruption, and monitor constraints. WorkbenchLayoutsUiTests covers fixed identity, rejected drafts before switch, hidden draft retention, management, floating close, and language.

MainDockContentUiTests checks real initial Standard active Views/local models/input/timeline/effects attachment. InitialWorkspaceMaterializesWithoutTheDeferredPresentationQueue sets inherited deferral to one day and still requires synchronous root/split/ToolChrome/panel attachment, without resetting global scheduling or hiding races via Loaded/layout switches.

Historical macOS WorkspaceProbe checked initial active Views, preset cycles, fixed identity, close/reopen, three-window native-button properties/menu modes, visual trees/dimensions/models. Report: artifacts/verification/workspace-macos-debug.json. This is distinct from Headless.

Drag/tab merging, screens/DPI, real native clicks/fullscreen need actual interaction. Windows button clicks have later separate [release evidence](README.md#implementation-evidence); original geometry/property checks alone did not prove them.
