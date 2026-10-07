---
name: aeginext-localization
description: Add, change, review, or debug AegiNext UI localization using external JSON language packs, the shared Localization service, and live Avalonia Loc bindings. Use for translated controls, dynamic view-model text, menus, window titles, language preferences, and language-resource packaging; subtitle content, user preset names, and effect-script syntax remain business data.
---

# AegiNext localization

Locate the current AegiNext checkout and read the actual consumer and related code first. Resolve repository paths against that checkout; do not embed developer-machine paths. The shared entry point is `src/AegiNext.Desktop/I18n/`, with namespace `AegiNext.Desktop.I18n`. Extend this existing system for new UI text.

## Choose context for the change

| Change | Read first |
|---|---|
| Text, language packs, missing entries, or language matching | `I18n/Languages/*.json`, `I18n/Localization.cs`, `I18n/LocalizationCatalog.cs`, `I18n/LanguagePackReader.cs` |
| Static labels, tooltips, or accessibility names | Consumer AXAML, `I18n/LocExtension.cs`, `I18n/XamlNamespace.cs`, `Controls/Common/IconText.cs` |
| Dynamic state, menus, Dock, or window titles | Owning ViewModel/controller, `Workspace/WorkbenchSession.cs`, `Windowing/WorkbenchWindowRegistry.cs` |
| First display, language choices, or preferences | `App.axaml.cs`, `Views/MainWindow.axaml.cs`, `Workspace/WorkbenchStartupPreferences.cs`, `Settings/Appearance/AppearanceSettingsViewModel.cs` |
| Build or publish resources | `AegiNext.Desktop.csproj`, `scripts/publish/AegiNext.Publish.psm1`, `docs/en/publishing.md` |

Production paths without a full prefix are under `src/AegiNext.Desktop/`; scripts and documentation resolve from the checkout root. See `docs/en/composable-workspace.md` for workspace localization. Read [references/verification.md](references/verification.md) when verification is needed; ordinary text edits do not require loading every implementation.

## Language packs and keys

Edit source packs in `I18n/Languages/`, not copied output. Keep builtin `en-US.json` and `zh-CN.json` key sets equal and add both translations for new keys. Third-party packs may omit entries and use fallback. Preserve owning prefixes such as `Workbench`, `Settings`, `Preview`, `Layout`, `Log`, `WorkflowLog`, and `WindowChromeProbe`. Keep semantically distinct keys, such as `Workbench.Export` and `Settings.Export`.

```json
{
  "LanguageName": "English",
  "LanguageID": "en-US",
  "Strings": {
    "Workbench.Cancel": "Cancel"
  }
}
```

This illustrates the schema, not a replacement builtin pack. Save UTF-8 without a BOM. `LanguageName` must be nonempty, `LanguageID` a valid culture identifier, and every nonempty `Strings` key must map to a string. `system` is reserved as a selection value. Reject duplicate root fields and text keys. Language IDs are normalized and compared case-insensitively; text keys are case-sensitive. Preserve positional argument indices and formatting semantics in translations.

Startup scans only first-level `*.json` files and uses their internal IDs. Malformed optional packs produce file diagnostics and are excluded; every pack in a duplicate-ID group is excluded. Startup fails without a valid unique `en-US` pack. Do not hide resource failures with duplicate C# text tables. Resource changes require restart; this system has no hot reload.

Subtitle content, user preset names, paths, script syntax, serialized IDs, and encoder identities retain their business meaning. Enum-to-key mappings belong to the feature, for example `Editing/AnimationPropertyLocalization.cs`, rather than expanding the general service.

## Service and preference contracts

- Call `Initialize(directory)` once on the UI thread before loading AXAML, using `Path.Combine(AppContext.BaseDirectory, "i18n")`. It captures the original system UI culture; do not initialize per control/window.
- `Get(key)` reads immutable snapshots from any thread: current language, then `en-US`, then the raw key. Results do not depend on the caller's `CurrentUICulture`; asynchronous playback callbacks use the same service.
- `Format(key, params object[] arguments)` uses `CurrentCulture` for numeric formatting. Language changes update UI culture without changing established numeric-input culture.
- `SetLanguage(lang)` runs on the UI thread and accepts installed IDs or `system`. Unknown IDs throw without changing language; unchanged selection/effective state does not notify again.
- `SelectedLanguageID` is the requested selection; `CurrentLanguageID` is the resolved pack. System matching uses the initially captured culture: exact ID, parents, same-language candidates, then English. Chinese/English prefer `zh-CN`/`en-US`; other candidates sort by ID.
- `KnownLanguages` supplies `LanguageName` labels and `LanguageID` selection, plus a localized system option. Do not restore fixed English/Chinese indices or a preference whitelist.
- The service does not persist preferences. Apply the same loaded startup preference before MainWindow AXAML, and pass that store/snapshot into the session. If a saved pack is temporarily unavailable, display English while preserving the saved ID and other preferences.
- After a direct `SetLanguage`, saving an unrelated theme/volume preference must not reset language. Apply a new selection only when the language preference itself changes.

## Use Loc for static text

The default Avalonia XML namespace registers `LocExtension` through `XmlnsDefinition`, so use `{Loc Key=...}` without a prefix:

```xml
<TextBlock Text="{Loc Key=Workbench.Codec}" />
<Button Content="{Loc Key=Workbench.Cancel}" />
<Expander Header="{Loc Key=Settings.Advanced}" />
<Button ToolTip.Tip="{Loc Key=Workbench.Save}">
    <common:IconText Text="{Loc Key=Workbench.Save}" IconKey="Save" />
</Button>
```

The last example needs `xmlns:common="using:AegiNext.Desktop.Controls.Common"`. Reuse IconText typography and sizing; `IconKey` identifies the icon independently of translated keys/values. Migrate Text, Content, Header, tooltips, and accessibility names as appropriate; preserve business bindings for dynamic content.

Loc returns an observable binding, immediately supplies text on subscription, and updates on language changes. Preserve DataContext. Do not capture target controls, scan trees, reintroduce translation Tags, or replace live bindings with one-time Get assignments.

## Dynamic text and subscription lifetime

Play/pause, mute, shortcut recording, and export states stay bound to their owning ViewModels, which call Get/Format. Store semantic keys, enums, and format arguments rather than only the last translated string. Reuse centralized LanguageChanged handlers in sessions, layouts, registries, and settings; avoid duplicate subscriptions.

For programmatic controls, retain and release the observable binding handle:

```csharp
var binding = button.Bind(
    ContentControl.ContentProperty,
    Localization.Observe("Workbench.Cancel").ToBinding());
```

Observe is currently internal to Desktop. Release handles/events on Closed/Dispose and dispose a replaced state binding first. Providers capture only necessary business values, as in `Views/TrackStyleChangeDialog.cs`. Distinguish temporary fixed-panel detach from final disposal so reattachment still receives updates.

Language refresh changes presentation only: no business commits, project recreation, Undo, or discarded drafts. Refresh choice lists while preserving stable IDs, actual selection, and invalid raw input; ignore framework automatic selection as a user edit. Reuse the consuming Styles/Effects ChoicesRefreshing/ChoicesRefreshed guards where needed.

## Verify and deliver

Follow the affected groups in [verification](references/verification.md), checking compiled AXAML, live switching, business state, and release behavior. Inspect raw JSON for key completeness; Get fallback cannot prove translations exist. Build/loading/publishing changes require actual output i18n directories, package hashes, and startup from another working directory. macOS packages use `AegiNext.app/Contents/MacOS/i18n/`.

Report actual builds/analysis, passed/failed/skipped tests, resource checks, and native acceptance. Recount keys/tests from current evidence rather than historical totals. Separate Headless results from button/icon, long-text, and platform-menu appearance. Preserve scope and unrelated work; using this skill does not authorize commits or dependency upgrades.
