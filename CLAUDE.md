# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Scalpel is a local-only, portable Windows PDF editor (view, annotate, merge/split, edit text, sign, fill forms, print, flatten). It ships as a single self-installing ~6 MB EXE with no runtime install and no telemetry. GPLv3.

## Build / run / test

```powershell
dotnet build                      # debug build
dotnet publish -c Release         # single-file Costura EXE -> bin/Release/net48/publish/
dotnet test                       # run xUnit tests (Scalpel.Tests)
dotnet test --filter "FullyQualifiedName~SearchService"   # single test class
```

- Targets **.NET Framework 4.8** (`net48`) but **requires the .NET 8 SDK or later to build**. Output is x64. `dotnet` may not be on PATH; a user-local SDK at `~/.dotnet/dotnet.exe` works.
- Build gotcha: after a `dotnet publish` (which pins the `win-x64` RID), a later `dotnet build --no-restore` can fail `NETSDK1047` ("no target for net48/win7-x64"). Re-run **with** restore (drop `--no-restore`) to fix.
- `dotnet publish` also runs `build/bundle-source.ps1` (the `BundleSource` MSBuild target) to produce a GPL3 `Scalpel-<version>-src.zip` alongside the EXE.
- `release.ps1` is the full release pipeline (build → Authenticode sign → `signtool verify /pa` gate → SHA256 → write `BuildInfo.cs` → summary). Don't run it for ordinary dev work; it expects signing certs/SimplySign.

### MSIX / Microsoft Store package

```powershell
pwsh -File packaging\build-msix.ps1 -SelfSign   # local sideload test package (self-signed)
pwsh -File packaging\build-msix.ps1 -NoSign -IdentityName ... -Publisher ... -PublisherDisplayName ...   # Store submission
```

`packaging/build-msix.ps1` publishes the EXE, stages a layout, runs `makepri`/`makeappx`, and signs. Needs the Windows 10/11 SDK (`makeappx`, `signtool`). See `docs/STORE-PUBLISHING.md`. The same `Scalpel.exe` goes inside the package; only the manifest (`packaging/AppxManifest.xml`, `{token}`-substituted) and assets are added.

## Architecture

WPF desktop app. There is no MVVM framework in play for the main window — it is a large code-behind file.

- **`MainWindow.xaml.cs` (~9200 lines, 440 KB) is the monolith.** Nearly all editing, rendering, navigation, search, form-filling, signing, cropping, and save logic lives in this one `partial class MainWindow`. When adding UI behavior, expect to work here. `MainWindow.xaml` is the matching layout.
  - **Mode-tab UI ("Studio" redesign):** the toolbar is organized by a `AppMode { View, Edit, Pages, Sign }` enum. `SetMode(AppMode)` toggles the visibility of four `ModePanel{View,Edit,Pages,Sign}` panels and the four mode-tab `IsChecked` states (guarded by `_suppressModeEvents`). The tabs are grouped `RadioButton`s (can't be deselected by clicking the active one). A persistent File group (Open/Save/Print + a "File ▾" overflow menu for New/Close/Save Flattened) and Zoom group flank the swappable mode panels; Search + Settings sit in the tab strip. View modes (Single/Continuous/Two-page/Grid) live in the **View** tab (no longer in Settings). Tool buttons stay plain `Button`s so `SetTool`'s active-tool highlight (`SetResourceReference` on Background/Foreground) keeps working.
- **`App.xaml.cs`** is the entry point AND the installer. It handles crash dialogs (3 unhandled-exception sinks), `/uninstall`, per-user self-install to `%LOCALAPPDATA%\Programs\Scalpel` with `.pdf` file-handler registration (no UAC), the pdfium integrity check, settings, and temp-file lifecycle.
  - **Packaged-aware:** `App.IsPackaged()` (via `GetCurrentPackageFullName`) detects an MSIX/Store install. In packaged mode the self-installer is suppressed — portable badge hidden (`IsPortable()` returns false), `InstallAndRelaunch()` no-ops, `/uninstall` ignored — because the OS/package owns install, uninstall, and the `.pdf` association (declared in the manifest). Registry/AppData calls still work via MSIX virtualization. Keep new install-side behavior behind this gate.

### Three PDF libraries, three distinct roles

Do not assume one library does everything — they are deliberately split:

- **Docnet.Core** (bundles `pdfium.dll`) — **rendering** pages to bitmaps, plus encryption stripping and damaged-file repair via rasterization (`RepairViaDocnetRasterize`). PDFium also recovers files PdfSharpCore's parser rejects.
- **PdfSharpCore** — reading/writing PDF document **structure**: opening (`PdfReader.Open`), merge/split, page ops, and burning annotations on save. Note: it can *read* encrypted PDFs but cannot *re-save* them once modified, so the code decrypts to a temp copy first.
- **PdfPig** (`UglyToad.PdfPig`) — **text extraction** for full-text search (`Services/SearchService.cs`). Also referenced by the test project.

The open path in `MainWindow` has layered fallbacks (Modify → ReadOnly → Import-copy → PDFium rasterize-rebuild) because real-world PDFs are frequently malformed; preserve this fallback chain when touching file loading.

### Annotations are overlays burned in on save

`Models/EditingTypes.cs` defines the annotation model (`TextAnnotation`, `InkAnnotation`, `HighlightAnnotation`, `TextEditAnnotation`, `SignatureAnnotation`, `ImageAnnotation`, all `PageAnnotation` subtypes; `EditTool` enum). Annotations are live WPF overlays while editing and are rasterized/drawn into the PDF only on save (text edits white-out the original bounds and redraw). `PageThumbnailVm.cs` backs the sidebar page list.

### Themes and localization are hot-swappable ResourceDictionaries

- `Services/ThemeManager.cs` — 6 themes (`Themes/*.xaml`). **Index 0** of `Application.Current.Resources.MergedDictionaries` is the theme dict; it is updated **in place per-key** (not by structural add/remove) to avoid `ResourceReferenceKeyNotFoundException` during live switches. Because the update copies the *new* dict's keys, **every theme file must define the identical key set** (`Dark`, `Light`, `HighContrast`, `Blood`, `Greed`, `Cyanotic`) — a key missing from one theme silently keeps its stale value on switch. Also sets the DWM dark title bar via P/Invoke.
- `Services/LocaleManager.cs` — strings live in `Strings/*.xaml` (en-US, es, zh-TW, zh-CN, bn, tr-TR). **Index 1** of the merged dictionaries is the strings dict (replaced wholesale on switch — so **every** key must exist in **every** locale file, or a `DynamicResource` lookup blanks out in that language). Adding a language = `Strings/<locale>.xaml` + a `Locale` enum entry + a `pack://` URI case in `LocaleManager` + a Settings radio button in `MainWindow.xaml` with a `Lang…Radio_Checked` handler and a sync line in `SettingsBtn_Click` + a `Str_Lang_<name>` key in all six locale files. See `Strings/TRANSLATING.md`.
- **Index 2 = `Themes/_Shared.xaml`** (merged in `App.xaml`, after theme[0]/strings[1]): the non-color "Studio" design system — bundled `FontUI` (Geist) / `FontIcon` (Tabler, subset) `FontFamily` resources, the `Fs*` type-scale doubles, the `Ico_*` Tabler glyph-string map (referenced by name, e.g. `{StaticResource Ico_Save}` / `(string)FindResource("Ico_Save")`), and the reusable control styles (`StudioToolButton`, `StudioPrimaryButton`, `StudioDangerButton`, `StudioModeTab`, `StudioIconButton`, `StudioToolToggle`, `StudioPill`, `StudioSwatch`, `StudioOverlayCard`, plus implicit `ScrollBar`/`ContextMenu`). Keep the 0/1/2 index order intact. Fonts are in `Resources/Fonts/*.ttf` as `Resource` build items (the Tabler subset is ~12 KB / 39 glyphs — re-subset via `python -m fontTools.subset` if you add a glyph); colors always come from the theme tokens via `DynamicResource`.

Both managers persist the user's choice and restore it at startup (`Initialize()` is called from `App.OnStartup` before `MainWindow`).

### Persistence

- **Settings** → Windows registry under `HKCU\Software\Scalpel\Settings` (`App.GetSetting` / `SetSetting`). Install/handler state lives under `HKCU\Software\Scalpel` and the standard Uninstall key.
- **Saved signatures** → `%LOCALAPPDATA%\Scalpel\signatures.json` (`Services/SignatureStore.cs`, `System.Text.Json`).
- **Temp files** → `scalpel_*.pdf`, registered per-session and swept on startup/exit (`CleanupSessionTemps` / `CleanupStaleTemps`).

### pdfium integrity check

`BuildInfo.cs` holds the expected SHA256 of `pdfium.dll`. At startup `CheckPdfiumIntegrity()` decompresses the Costura-embedded pdfium resource and compares hashes; mismatch aborts the app. All-zeros (`PdfiumSha256Disabled`) disables the check for dev/`SkipSign` builds, and dev builds running loose from `bin/` (not Costura-bundled) skip it too. `release.ps1` writes the real hash.

## Conventions

- C# with `Nullable` enabled and `ImplicitUsings` enabled; `LangVersion=latest`. Collection expressions (`[]`), target-typed `new`, and `switch` expressions are used throughout — match that style.
- I/O and parsing are wrapped in defensive `try { } catch { }` that swallow and fall back (PDFs are untrusted/malformed); follow this pattern rather than letting exceptions reach the user mid-edit.
- Tests (`Scalpel.Tests`) are xUnit and **link the source files directly** (`<Compile Include="..\Services\...">`) rather than referencing the WinExe project. If you move a tested file, update the `.csproj` link paths.

## Further docs

- `docs/OVERVIEW.md` — full architecture/what-it-does reference.
- `docs/UI-REFERENCE.md` — every button/control, mapped to its handler.
- `docs/DESIGN-SYSTEM.md` — the "Studio" design system: tokens, type scale, icons, components, layout, and how to extend.
- `docs/STORE-PUBLISHING.md` — MSIX build + Store submission (incl. the GPLv3-on-Store licensing note).
- `docs/LOGGING.md` — the local-only JSONL session logging system: per-user log location, format, categories, retention, and QA usage.
