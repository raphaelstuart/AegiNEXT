# Localization verification

Resolve paths against the current AegiNext checkout. Select affected tests and confirm that the named classes still exist; selecting zero tests is not success. Repeat wider regression only for new failures or unresolved concerns.

## Choose the test group

| Change | Entry points and assertions |
|---|---|
| JSON, loader, fallback, system matching | `Tests/AegiNext.Desktop.Tests/LocalizationCatalogTests.cs`: third-language discovery, metadata, duplicate fields/keys/IDs, invalid English, missing-key fallback, unknown IDs, and system matching |
| Consumers and mappings | `WorkbenchLocalizationTests`, `SettingsLocalizationTests`, relevant `EffectScriptLanguageTests` / `ColorDraftTests` |
| AXAML, tooltips, accessibility, templates, menus, icons | `Tests/AegiNext.Desktop.Ui.Tests/LocalizationUiTests.cs` and `LocalizationFixtureView.axaml`: assert initial text, switch language, assert updates and intact DataContext/commands/business bindings |
| Async playback and thread culture | `PreviewLanguageUiTests`, `LocalizationUiTests`: change callback UI culture after selecting a language; verify service-snapshot text and unchanged numeric formatting culture |
| First display, preferences, missing packs, stable choices | `SettingsStartupUiTests`, `SettingsWindowUiTests`, `ShortcutSettingsRecordingUiTests`, related settings tests |
| Menus, Dock, titles, closing | `LocalizationUiTests`, `NativeMenuLifecycleUiTests`, `WorkbenchLayoutsUiTests`: direct SetLanguage refresh and subscription cleanup |
| Drafts and business state | Relevant Draft tests and `LocalizationUiTests`: retain invalid raw input, selected IDs, snapshots, and Undo; switching must not edit content or save preferences |
| Publishing | `Tests/Build/AegiNext.Publish.Tests.ps1`, `scripts/publish/verify-package.ps1`: directories, builtin metadata, BOM-free UTF-8, duplicates, and hashes |

Run relevant commands, not every group. Restore normally if outputs are absent; use `--no-build` only when the current source is compiled in the matching configuration.

```sh
dotnet test Tests/AegiNext.Desktop.Tests/AegiNext.Desktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~LocalizationCatalogTests|FullyQualifiedName~WorkbenchLocalizationTests|FullyQualifiedName~SettingsLocalizationTests'
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj --no-restore --filter 'FullyQualifiedName~LocalizationUiTests|FullyQualifiedName~PreviewLanguageUiTests|FullyQualifiedName~SettingsStartupUiTests'
dotnet build src/AegiNext.Desktop/AegiNext.Desktop.csproj -c Release --no-restore
git diff --check
git diff --cached --check
```

Compile changed UI tests as well; building the app does not establish fixture AXAML compilation. When Rider MCP is available, analyze affected C#/AXAML and report the actual severity and findings.

## Resource completeness and old entry points

- Compare raw builtin Strings key sets, consumer keys, and positional format arguments. Get fallback does not replace this check.
- Replace old Tag-extraction coverage with actual Loc, programmatic binding, and business mappings; assert the discovered set is nonempty. Check dynamically composed keys through their finite mappings.
- Change migration hashes/count expectations only for authorized text changes, never merely to suppress failures or prohibit future keys.
- Use loader tests for duplicates. Ordinary dictionaries may overwrite duplicate fields, so inspecting the final dictionary alone is insufficient.
- Search legacy entry points: `ControlLocalization`, `SettingsViewLocalization`, `AegiNext.Desktop.Localization`, `WorkbenchText`, `SettingsText`, `PreviewText`, `LayoutText`, `LogText`, `WorkflowLogText`, `WindowChromeProbeText`. Business Tags may remain. `WorkbenchTextFormatting` is typography support, not an old text table.

## Static state and window cleanup

Initialize through LocalizationTestStartup/UI bootstrap, without a public reset API or repeated Initialize in one process. Isolated loading tests use TemporaryLocalizationDirectory and LocalizationCatalog. Run tests touching static language/culture/environment serially; restore only changed state in finally/Dispose, restoring language before the cultures synchronized by the service.

Use UiTestEnvironment and MainWindowTestContext for isolated preferences, sessions, and windows; close standalone hosts in finally. For subscriptions, open/switch/close the actual object and verify no later updates. GC checks use a separate create/close helper and eliminate strong references. Close the owner tree and account for Avalonia's last-focused-window cache with a plain-window control when necessary. Do not remove release assertions or clear all service events to conceal retained objects.

## Build and publish changes

A translation-only edit does not require rebuilding native dependencies. For copy/loading/publishing changes:

1. Check Desktop language items still have `TargetPath=i18n/%(Filename)%(Extension)`, CopyToOutputDirectory, and CopyToPublishDirectory.
2. Inspect actual build/test/publish language outputs and compare source metadata/content/hashes. Resolve via AppContext.BaseDirectory, not the working directory.
3. Run affected publishing Pester tests and PSScriptAnalyzer for changed PowerShell; use the existing package verifier:

   ```sh
   pwsh -NoProfile -File ./scripts/publish/verify-package.ps1 -PackageDirectory "/absolute/path/to/package"
   ```

4. Verify generated JSON is in signing/hash inventories. Editing a published payload invalidates its manifest; use normal publishing to regenerate it.
5. Launch an owned test process from another working directory with isolated AEGINEXT_PREFERENCES_DIRECTORY, record startup/resource behavior, then close it. Preserve the user's processes/preferences.

## Evidence boundaries

Report passed, failed, and skipped tests separately. Investigate attribution before excluding failures; reproduce against isolated HEAD when necessary, preserving the workspace.

Headless proves bindings, state, measured layout, and lifetime; native startup checks resource location/basic startup. Neither proves final translated icons, long-text clipping, native macOS menus, or other platforms. Complex native appearance requires user interaction/screenshots and a separate acceptance result.
