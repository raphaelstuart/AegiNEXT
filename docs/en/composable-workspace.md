# Workspace integration

[English](composable-workspace.md) · [简体中文](../zh-cn/composable-workspace.md) · [All guides](README.md)

## Put changes in the owning layer

Paths below are under `src/AegiNext.Desktop/`.

| Directory | Owner |
|---|---|
| `Workspace/` | One WorkbenchSession, editor/controllers, selection, drafts, and awaitable workflows |
| `Layouts/` | Dock tree, floating hosts, layout snapshots, and presets |
| `Panels/` | Feature Views and ViewModels |
| `Controls/Common/`, `Controls/Editing/`, `Editing/` | Reusable controls and field/gesture editing behavior |
| `Settings/` | Personal libraries/preferences and their drafts |
| `Views/`, `Windowing/` | Hosts, platform chrome, menus, and input coordination |
| `I18n/` | Language service, JSON packs, and live bindings |

Read the consuming panel and closest shared control before adding an API. Shared controls expose values, drafts, commands, and completion/cancel events; they do not acquire a session or depend on Dock. ViewModels contain business state rather than controls/pixels.

## Integrate a panel or control

1. Add the View/ViewModel in the feature owner and compose dependencies in Workspace.
2. Supply one fixed View per stable `WorkbenchPanelIds` entry to the layout controller; docking/hiding never recreates it.
3. Route real input through shared commands and preserve local text/IME behavior.
4. Commit valid drafts before changing editing targets; invalid raw input retains the target and Esc restores one field.
5. Give each completed gesture one transaction; cancel on capture loss or target changes. Close drains asynchronous work and disposes owned resources.

Layouts control space without owning editor, Undo, playback, or export. Current layout and named presets are independent. Persistence validates app-owned snapshots, writes atomically, and constrains restored windows to available monitors. Floating close hides panels; main close flushes layouts and drains the session.

Font fields share `FontFamilyPicker`: the current family stays first, multiple variants use a submenu, and a single variant commits directly. Menu matching ignores optional PostScript metadata while committed selections retain the complete font identity. Popup input belongs to its panel through both visual and logical ancestry.

Hosts inject `IFontNamePreviewProvider` from the shared font service. `Controls/Media/FontNamePreviewPresenter` requests visible names and owns its bitmap; detach cancels its waiter and rejects stale results. Rendering opens the exact font face or named variable instance and returns a theme-independent Alpha8 mask. `DesktopApplicationContext` owns the bounded preview cache under `PreferencesStore.DirectoryPath/caches/fonts/v1`, keyed by font content and instance, name, size, and DPI; controls never access disk or create their own cache.

## Window appearance and input

Reuse shared typography, spacing, panel corners, and chrome. Layout owns outer frames; panels avoid duplicate borders. Register floating/settings hosts for the same menu/shortcut policy.

macOS defaults to system menus; window-menu mode shows menus on the main workbench. Windows chrome integrates native caption actions and resizing. Theme/shortcut refreshes preserve command identity; language regrouping waits for safe menu lifecycle points.

## User settings backup and migration

Export a `.aegisettings` bundle from the Settings import/export page. The bundle includes all committed preferences, personal subtitle styles with embedded fonts, personal effect scripts, encoding presets, the current workspace layout, and personal layout presets. Builtin scripts and layouts, recent projects, caches, and project assets are excluded. Export does not commit project drafts or include unsaved preset drafts.

Import validates the complete bundle and previews its personal preset counts before scheduling restoration at the next launch. Migration keeps this device's default project directory by default; disable that option to restore the bundled directory, which must be valid on the destination system. Audio calibration remains scoped to an exactly matching device chain. After import, a restart prompt can close the application through its normal exit workflow, including existing unsaved-project confirmation. Deferred restoration can be cancelled from the settings page.

`Settings/Transfer/` composes existing storage contracts without introducing Desktop dependencies into Application or Media. The pending bundle does not replace files while the application is running. Before loading preferences and libraries on the next launch, restoration backs up the original bytes and replaces the files as one journaled transaction. Failed or interrupted restoration rolls back the old files and retains the backup and pending bundle for retry. Builtin resources remain supplied by the application.

## Localization

Language packs remain external UTF-8 JSON in `I18n/Languages/`, copied to application `i18n/`. Each has `LanguageName`, `LanguageID`, and string-valued `Strings`; language IDs and keys must be unique.

- C#: `Localization.Get("Key")` and `Localization.SetLanguage(...)`.
- XAML: `{Loc Key=...}` binds live through the markup extension.
- Lookup: current language → `en-US` → key.
- Discover available packs automatically; preserve system-language matching and preferences.
- Keep subtitle content, preset names, and DSL syntax as business data.

Do not add Tag/tree scanning. Pack changes need restart; publishing validates and hashes them. Portable guidance is in [controls](../../.agents/skills/aeginext-controls/SKILL.md) and [localization](../../.agents/skills/aeginext-localization/SKILL.md) skills.

## Verify integration

Run affected Desktop controller tests and Desktop.Ui Headless tests. Use actual Views, pointer/key input, and deterministic service boundaries; close every created host. Native menus, drag/docking, fullscreen, multiple monitors, DPI, and IME appearance need platform interaction checks.
