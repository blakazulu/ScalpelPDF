# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Scalpel is a local-only, portable Windows PDF editor (view, annotate, merge/split, edit text, sign, fill forms, print, flatten), plus a **Tools** menu of document operations: page/Bates numbering, compression, OCR, password protection, and metadata removal. It ships as a single self-installing ~6 MB EXE with no runtime install and no telemetry. GPLv3.

## Build / run / test

> **📦 Releasing/publishing any channel (portable EXE, Inno installer, or Microsoft Store)? STOP and follow [`docs/RELEASING.md`](docs/RELEASING.md) — the canonical runbook.** It covers version bumping (the two sources of truth + pre-push auto-bump), the exact build commands, the GitHub-Release/Netlify/`version.json` distribution wiring, the publish order (website **last**), signing posture, Store-identity verification, and every gotcha. Do not improvise a release.

```powershell
dotnet build                      # debug build
dotnet publish -c Release         # single-file Costura EXE -> bin/Release/net48/publish/
dotnet test                       # run xUnit tests (Scalpel.Tests)
dotnet test --filter "FullyQualifiedName~SearchService"   # single test class
```

- Targets **.NET Framework 4.8** (`net48`) but **requires the .NET 8 SDK or later to build**. Output is x64. `dotnet` may not be on PATH; a user-local SDK at `~/.dotnet/dotnet.exe` works.
- Build gotcha: after a `dotnet publish` (which pins the `win-x64` RID), a later `dotnet build --no-restore` can fail `NETSDK1047` ("no target for net48/win7-x64"). Re-run **with** restore (drop `--no-restore`) to fix.
- Build gotcha: a `dotnet build` copy failure on `pdfium.dll` (`MSB3027`/`MSB3021`) usually just means a running `Scalpel.exe` is locking it — close the app and rebuild; it is not a code error.
- `dotnet publish` also runs `build/bundle-source.ps1` (the `BundleSource` MSBuild target) to produce a GPL3 `Scalpel-<version>-src.zip` alongside the EXE.
- `release.ps1` is the full release pipeline (build → Authenticode sign → `signtool verify /pa` gate → SHA256 → write `BuildInfo.cs` → summary). Don't run it for ordinary dev work; it expects signing certs/SimplySign.

### MSIX / Microsoft Store package

```powershell
pwsh -File packaging\build-msix.ps1 -SelfSign   # local sideload test package (self-signed)
pwsh -File packaging\build-msix.ps1 -Store      # Store submission (bakes in the real Partner Center identity, unsigned)
```

`packaging/build-msix.ps1` publishes the EXE, stages a layout, runs `makepri`/`makeappx`, and signs. Needs the Windows 10/11 SDK (`makeappx`, `signtool`). **For a real submission use `-Store` and follow [`docs/RELEASING.md`](docs/RELEASING.md) §5 — verify the Partner Center identity (the `PublisherDisplayName` changes if you rename your publisher) before every upload.** See also `docs/STORE-PUBLISHING.md`. The same `Scalpel.exe` goes inside the package; only the manifest (`packaging/AppxManifest.xml`, `{token}`-substituted) and assets are added.

## Architecture

WPF desktop app. There is no MVVM framework in play for the main window — it is a large code-behind file.

- **`MainWindow.xaml.cs` (~9200 lines, 440 KB) is the monolith.** Nearly all editing, rendering, navigation, search, form-filling, signing, cropping, and save logic lives in this one `partial class MainWindow`. When adding UI behavior, expect to work here. `MainWindow.xaml` is the matching layout.
  - **Ribbon UI ("Clinical" redesign — see `docs/system-design.md` for the full spec):** the chrome is a Microsoft-Office-style **ribbon**, organized by an `AppMode { View, Edit, Pages, Sign }` enum. The window grid is 5 rows: **title bar + Quick Access Toolbar** (`QatButton`s: Open/Save/Print/Undo, plus Search/Settings; the `SaveAsBtn` icon shows the dirty state) → **ribbon tab strip** (the four mode tabs as grouped `RadioButton`s styled `RibbonTab`, plus Tools/About/What's New on the right) → **ribbon band** (the four `ModePanel{View,Edit,Pages,Sign}` panels, each laid out as captioned **groups** of vertical `RibbonButton`/`RibbonToggle` command buttons) → content → **status bar** (status text + the relocated `ZoomBox`/`ZoomOutBtn`/`ZoomInBtn` zoom cluster). `SetMode(AppMode)` still just toggles the four `ModePanel*` visibilities and the tab `IsChecked` states (guarded by `_suppressModeEvents`); `SetTool` still highlights the active tool via `SetResourceReference(Background→AccentDim, Foreground→Accent)`. **The ribbon is a pure view restructure — every `x:Name` and Click handler from before is preserved**, so the code-behind was unchanged. View modes (Single/Continuous/Two-page/Grid) are the **View** tab's Layout group.
- **`App.xaml.cs`** is the entry point and orchestrator. It handles crash dialogs (3 unhandled-exception sinks), delegates install/uninstall to `Services/Installer.cs` and `Services/InstallerUI.cs`, and manages the pdfium integrity check, settings, and temp-file lifecycle.
  - **`Services/Installer.cs`** (WPF-free) is the canonical install/uninstall engine: owns `OwnedRegistryKeys`, `OwnedRegistryValues`, and `OwnedPaths` (the complete cleanup inventory), `WipeAllData()` (removes registry keys/values, shortcuts, `%TEMP%` scratch, fires `SHChangeNotify`), and `WriteDeferredDirWipeScript()` (a deferred `.bat` that removes BOTH `%LOCALAPPDATA%\Programs\Scalpel` and `%LOCALAPPDATA%\Scalpel` after the EXE exits — **zero-leftover uninstall**). Path constants for the install dir also live here. Publisher name in Add/Remove Programs is "Liraz Amir".
  - **`Services/InstallerUI.cs`** owns the branded install/uninstall dialogs (fixed dark+amber palette, Geist/Tabler fonts, custom chrome): `ShowInstallConfirm(...)` (with desktop-shortcut toggle) and `RunUninstallFlow(...)` (confirm → progress → farewell that auto-closes).
  - **Packaged-aware:** `App.IsPackaged()` (via `GetCurrentPackageFullName`) detects an MSIX/Store install. In packaged mode the self-installer is suppressed — portable badge hidden (`IsPortable()` returns false), `InstallAndRelaunch()` no-ops, `/uninstall` ignored — because the OS/package owns install, uninstall, and the `.pdf` association (declared in the manifest). Registry/AppData calls still work via MSIX virtualization. Keep new install-side behavior behind this gate.

### Three PDF libraries, three distinct roles

Do not assume one library does everything — they are deliberately split:

- **Docnet.Core** (bundles `pdfium.dll`) — **rendering** pages to bitmaps, plus encryption stripping and damaged-file repair via rasterization (`RepairViaDocnetRasterize`). PDFium also recovers files PdfSharpCore's parser rejects.
- **PdfSharpCore** — reading/writing PDF document **structure**: opening (`PdfReader.Open`), merge/split, page ops, and burning annotations on save. Note: it can *read* encrypted PDFs but cannot *re-save* them once modified, so the code decrypts to a temp copy first.
- **PdfPig** (`UglyToad.PdfPig`) — **text extraction** for full-text search (`Services/SearchService.cs`). Also referenced by the test project.

The open path in `MainWindow` has layered fallbacks (Modify → ReadOnly → Import-copy → PDFium rasterize-rebuild) because real-world PDFs are frequently malformed; preserve this fallback chain when touching file loading.

### Annotations are overlays burned in on save

`Models/EditingTypes.cs` defines the annotation model (`TextAnnotation`, `InkAnnotation`, `HighlightAnnotation`, `TextEditAnnotation`, `SignatureAnnotation`, `ImageAnnotation`, all `PageAnnotation` subtypes; `EditTool` enum). Annotations are live WPF overlays while editing and are rasterized/drawn into the PDF only on save (text edits white-out the original bounds and redraw). `PageThumbnailVm.cs` backs the sidebar page list.

### Document-operation tools (the Tools menu)

The Tools dropdown (right of the ribbon tab strip; handlers `ToolsNumbering/Compress/Ocr/Redact/Protect/Sanitize_Click`) runs document-level operations through standalone, mostly WPF-free services rather than the `MainWindow` monolith: `BatesNumberingService` (page/Bates numbers + corner headers/footers), `PdfCompressionService` (image downsampling, Low/Med/High), `RedactionService`, `PdfEncryptionService` (password + permissions), `MetadataSanitizer`, and OCR (`OcrService` + `TesseractCliOcrEngine`, page rasterization via `PdfRasterTools` / `DocnetPageRasterizer`).

- **OCR engine + language data location is resolved by `Services/OcrAssets.cs`:** an **installed** build is expected to ship `tesseract.exe` + `tessdata` in an `ocr` folder next to the EXE (`AppOcrDir = <AppDir>\ocr`); a **portable** build ships nothing OCR-related and downloads `tessdata` once into `%LOCALAPPDATA%\Scalpel\ocr` on first use. The engine is *located* (bundled → per-user → Program Files → PATH), never auto-downloaded/executed. **Gotcha:** make sure the release/installer pipeline actually stages that `ocr` folder for installed builds.

### Themes and localization are hot-swappable ResourceDictionaries

- `Services/ThemeManager.cs` — themes are now a **two-axis model**: a base theme (`enum Theme { Dark, Light, HighContrast }`) plus an accent (`enum Accent { Amber, Red, Green, Cyan }`). Base theme files `Themes/Dark.xaml`, `Light.xaml`, `HighContrast.xaml` own all surface tokens and include Amber as the built-in default accent. Accent is applied as a thin overlay dictionary on top for Dark/Light + non-Amber accents only: `Themes/Accents/{Dark,Light}_{Red,Green,Cyan}.xaml` (6 files). Amber = no overlay; High Contrast = no overlay (HC keeps its own fixed amber/white accent and ignores the accent picker). **Index 0** of `Application.Current.Resources.MergedDictionaries` is still updated **in place per-key** on switch. The "every file must define the identical key set" rule now applies **per layer**: all 3 base files share the full key set; all 6 accent overlay files share the 11-key accent token set. The **Clinical redesign** added a block of semantic tokens to each base file — `ChromeBg`/`ChromeText`/`ChromeTextDim`/`ChromeHover` (title + status bar), `RibbonBg`/`RibbonBand`/`RibbonGroupLabel`/`RibbonBtnHover` (the ribbon), and `ZoomTrack`; in **Light** these give a steel chrome over a paper-white ribbon (the "Clinical" look). The **Red** accent was retuned to **surgical red** (`#E11D38` light / `#F04458` dark). `ThemeManager` exposes `CurrentTheme`/`CurrentAccent` and `ApplyTheme(Theme)`/`ApplyAccent(Accent)`. `Initialize()` calls `Services/ThemeMigration.cs` to migrate legacy persisted values (Blood→Dark+Red, Greed→Dark+Green, Cyanotic→Dark+Cyan) — and a **fresh install now defaults to Light + Red** (the Clinical look), not Dark + Amber. Persists `Theme` + a new `Accent` registry key (both under `HKCU\Software\Scalpel\Settings`). Also sets the DWM dark title bar via P/Invoke.
- `Services/LocaleManager.cs` — strings live in `Strings/*.xaml` (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru). **Index 1** of the merged dictionaries is the strings dict (replaced wholesale on switch — so **every** key must exist in **every** locale file, or a `DynamicResource` lookup blanks out in that language). Adding a language = `Strings/<locale>.xaml` + a `Locale` enum entry + a `pack://` URI case in `LocaleManager` + a Settings radio button in `MainWindow.xaml` with a `Lang…Radio_Checked` handler and a sync line in `SettingsBtn_Click` + a `Str_Lang_<name>` key in all locale files. See `Strings/TRANSLATING.md`. **RTL locales:** Hebrew and Arabic mirror the whole UI — `LocaleManager.IsRtlLocale` drives `MainWindow.FlowDirection` (set in `ApplyInternal` on switch and re-applied in the `MainWindow` ctor since `Initialize()` runs before the window exists).
- **RTL/multilingual text in PDFs:** typing Hebrew/Arabic/Russian into annotations is burned in correctly at save by `DrawTextRun` (MainWindow.xaml.cs). Pipeline for RTL: `Services/ArabicShaper.cs` (cursive joining → Arabic Presentation Forms-B + lam-alef ligatures; PdfSharpCore does no GSUB shaping) → `Services/BidiReorder.cs` (logical→visual reorder; `IsRtl` covers Hebrew + Arabic ranges) → `PickFace` chooses a script-covering bundled font. Bundled faces: `NotoSansHebrew`, `NotoSansArabic`, `NotoSans` (Latin+Cyrillic, used so Russian doesn't render as `.notdef`) — all SIL OFL, registered in `App.RegisterPdfFonts()`. Known limitation: PdfSharpCore's ToUnicode CMap for subsetted Noto faces collapses, so full-text *search* over text Scalpel itself burned in is unreliable for Cyrillic/Arabic (rendering is correct; search of real-world logical-order PDFs is fine). Tests: `ArabicShaperTests`, `BidiReorderTests`, `FontEmbeddingTests`, `MultilingualPdfTests` (incl. a positional proof that שלום renders right-to-left, not reversed; sample at `docs/samples/multilingual-sample.pdf`).
- **Index 2 = `Themes/_Shared.xaml`** (merged in `App.xaml`, after theme[0]/strings[1]): the non-color "Studio" design system — bundled `FontUI` (Geist) / `FontIcon` (Tabler, subset) `FontFamily` resources, the `Fs*` type-scale doubles, the `Ico_*` Tabler glyph-string map (referenced by name, e.g. `{StaticResource Ico_Save}` / `(string)FindResource("Ico_Save")`), and the reusable control styles (`StudioToolButton`, `StudioPrimaryButton`, `StudioDangerButton`, `StudioModeTab`, `StudioIconButton`, `StudioToolToggle`, `StudioPill`, `StudioSwatch`, `StudioOverlayCard`, plus implicit `ScrollBar`/`ContextMenu`). The **Clinical ribbon** added `QatButton` (title-bar Quick-Access icon button), `RibbonTab` (mode tab that connects into the band with a top accent bar), `RibbonButton` (vertical icon-over-label command button — `Background`/`Foreground` are template-bound so `SetTool`'s highlight recolors icon + label), and `RibbonToggle` (the vertical view-mode toggle), plus non-localized `Grp_*` group-caption strings (`Grp_Layout`/`Display`/`Tools`/`History`/`Organize`/`Arrange`/`Signature`/`Forms`). Keep the 0/1/2 index order intact. Fonts are in `Resources/Fonts/*.ttf` as `Resource` build items (the Tabler subset is ~12 KB / 39 glyphs — re-subset via `python -m fontTools.subset` if you add a glyph); colors always come from the theme tokens via `DynamicResource`.

Both managers persist the user's choice and restore it at startup (`Initialize()` is called from `App.OnStartup` before `MainWindow`).

### Persistence

- **Settings** → Windows registry under `HKCU\Software\Scalpel\Settings` (`App.GetSetting` / `SetSetting`). Install/handler state lives under `HKCU\Software\Scalpel` and the standard Uninstall key.
- **Saved signatures** → `%LOCALAPPDATA%\Scalpel\signatures.json` (`Services/SignatureStore.cs`, `System.Text.Json`).
- **Temp files** → `scalpel_*.pdf`, registered per-session and swept on startup/exit (`CleanupSessionTemps` / `CleanupStaleTemps`).

### Brand assets (logo / icon)

The app icon is generated, not hand-edited. **`branding/` is the source of truth:** two vector
sources — `scalpel-icon.svg` (the full mark: a steel Fluent squircle + tilted document + a scalpel
with a surgical-red cutting edge) and `scalpel-glyph.svg` (a simplified glyph used ≤ 56px so it stays
legible in the taskbar). Pillow can't read SVG, so `*-master-1024.png` rasters are rendered from the
SVGs (headless Chrome) and committed. `branding/scalpel_logo.py --export` slices both masters into
every asset and **deploys** them: `Resources/scalpel.ico` (EXE `<ApplicationIcon>` + window `Icon=`;
small frames use the glyph), `packaging/Assets/*` (MSIX/Store tiles), and
`store-assets/StoreListingLogo_300x300.png`. Store screenshots (`store-assets/screenshots/*.png`,
1920×1080) are rendered from `design-mockups/store/shot.html`. **Full catalog + how to regenerate: `docs/system-design.md` §6.**

### pdfium integrity check

`BuildInfo.cs` holds the expected SHA256 of `pdfium.dll`. At startup `CheckPdfiumIntegrity()` decompresses the Costura-embedded pdfium resource and compares hashes; mismatch aborts the app. All-zeros (`PdfiumSha256Disabled`) disables the check for dev/`SkipSign` builds, and dev builds running loose from `bin/` (not Costura-bundled) skip it too. `release.ps1` writes the real hash.

## Conventions

- C# with `Nullable` enabled and `ImplicitUsings` enabled; `LangVersion=latest`. Collection expressions (`[]`), target-typed `new`, and `switch` expressions are used throughout — match that style.
- I/O and parsing are wrapped in defensive `try { } catch { }` that swallow and fall back (PDFs are untrusted/malformed); follow this pattern rather than letting exceptions reach the user mid-edit.
- Tests (`Scalpel.Tests`) are xUnit and **link the source files directly** (`<Compile Include="..\Services\...">`) rather than referencing the WinExe project. If you move a tested file, update the `.csproj` link paths.
- **User-facing changes (feature / feature-update / bug-fix) must add an entry to the in-app "What's New" popup** — prepend/extend a `Release` in `Services/Changelog.cs` (newest first; keep entries short and user-friendly).

## Further docs

- `docs/OVERVIEW.md` — full architecture/what-it-does reference.
- `docs/UI-REFERENCE.md` — every button/control, mapped to its handler.
- `docs/system-design.md` — **the current "Clinical" ribbon redesign:** layout, typography, full color-token tables (all themes + accents), component styles, code-behind contract, and the brand-asset catalog. Start here for UI/theme/asset work.
- `docs/DESIGN-SYSTEM.md` — the underlying "Studio" design-system mechanics (fonts, icons, type scale, `_Shared.xaml`); still accurate for those primitives, but the toolbar layout it describes is superseded by `system-design.md`.
- **`docs/RELEASING.md` — the canonical release runbook for all three channels (portable / installer / Store). Always follow it when publishing.**
- `docs/STORE-PUBLISHING.md` — MSIX build + Store submission deep-dive (incl. the GPLv3-on-Store licensing note).
- `docs/LOGGING.md` — the local-only JSONL session logging system: per-user log location, format, categories, retention, and QA usage.
