# Scalpel — Complete Overview

A full reference to what Scalpel is, what it does, and how it is built. For the short, working-context version aimed at AI assistants, see [`CLAUDE.md`](../CLAUDE.md). For end-user release notes, see [`CHANGELOG.md`](../CHANGELOG.md).

---

## 1. What it's for

Scalpel is a **local-only, portable PDF editor for Windows**, written as a reaction to Adobe Acrobat: no subscription, no account, no telemetry, no cloud, no file-association hijacking. The author's stated goal is "the PDF equivalent of Notepad" — a tool field techs can drop on a machine and use immediately.

Design pillars:

- **Single EXE, ~6 MB zipped.** Everything (including the native PDFium engine) is bundled into one executable. No runtime install — it targets .NET Framework 4.8, which ships with every supported Windows version.
- **Portable or self-installing.** Run the EXE directly, or let it install per-user to `%LOCALAPPDATA%` with no UAC prompt.
- **Offline and private.** Nothing phones home. All state lives in the registry and `%LOCALAPPDATA%`.
- **GPLv3.** Forks/redistributions must stay GPLv3 with source available; `dotnet publish` automatically produces a corresponding-source zip.

**Target platform:** Windows 10/11 x64. Landing page: [scalpel.example.com](https://scalpel.example.com). Distributed via direct download, `winget install scalpel`, and Chocolatey.

---

## 2. What it does (feature inventory)

### Viewing & navigation
- High-quality rendering via **PDFium**.
- **Four view modes** (persisted): Single Page, Continuous vertical scroll (progressive async render), Two-Page side-by-side, and Grid (whole-number columns; Ctrl+Scroll changes column count).
- Zoom: preset dropdown synced to scroll-wheel, Fit-to-Width and Fit-Page that re-apply on resize, zoom range 5%–max, Ctrl+= / Ctrl+− / Ctrl+0.
- Page-number jump box, arrow-key navigation, middle-mouse panning.
- **Outline/bookmark tree** (OUTLINES sidebar tab) and clickable internal links / cross-references / TOC back-links.
- Per-monitor DPI v2 aware.

### Editing & annotation
- **Inline text editing** of existing PDF text with font matching (whites out original bounds, redraws). Double-click placed text to re-edit.
- Free-standing **text boxes**, **freehand ink drawing**, and **highlight** overlays with adjustable color/size/opacity.
- **Signatures:** draw and save reusable signatures, or import PNG/JPG/BMP; click to place, drag a corner handle to scale.
- **Images:** place as resizable annotations, burned into the PDF on save.
- **Crop** tool with corner drag handles, editable CropBox coordinates, single-page or all-pages, rotation-aware.
- **Page operations** via right-click sidebar: insert blank, rotate CW/CCW, move up/down, extract, delete — works on multi-page selections.
- **Merge** multiple PDFs; **split** out selected pages; drag-and-drop page reordering.

### Forms, search, output
- **Form filling:** text inputs, checkboxes, radio buttons, and choice (`/Ch`) fields render as live WPF controls and are written back into the PDF (appearance streams regenerated).
- **Full-text search** across the document with highlighting and drag-select-to-copy.
- **Print** with a custom themed dialog + working preview (printer, orientation, copies, page ranges like `1-3,5`); annotations are flattened into the printout.
- **Save Flattened PDF:** rasterizes every page at 150 DPI (multi-core PNG encoding) into an uneditable document.
- **Password-protected PDFs:** prompts for the password instead of erroring.

### Presentation & i18n
- **Themes + accents** (live switch): three base themes (Dark, Light, High Contrast) × four accents (Amber, Red, Green, Cyan); accent is independent of base theme for Dark/Light; High Contrast uses a fixed accent.
- **Localized UI:** English, Spanish, Traditional Chinese, Simplified Chinese, Bengali, Turkish. Contributor guide in `Strings/TRANSLATING.md`.
- Keyboard-shortcut overlay (Ctrl+?), About dialog (click the version label).

---

## 3. How it's written

### Stack & toolchain
- **Language/runtime:** C# on **WPF**, targeting **.NET Framework 4.8** (`net48`), x64.
- **Build SDK:** requires the **.NET 8 SDK or newer** to build, even though output is `net48`. `PolySharp` + `Microsoft.NETFramework.ReferenceAssemblies` backfill modern language features (collection expressions `[]`, target-typed `new`, switch expressions) onto net48.
- **Single-file packaging:** **Costura.Fody** (+ Fody) embeds all managed and native dependencies into one `Scalpel.exe`.
- **Nullable** reference types and **ImplicitUsings** are enabled; `LangVersion=latest`.
- **MVVM toolkit:** `CommunityToolkit.Mvvm` is referenced but the main window is deliberately code-behind, not a formal MVVM architecture.

### NuGet dependencies that matter
| Package | Role |
|---|---|
| `Docnet.Core` (bundles `pdfium.dll`) | Page **rendering** to bitmaps; encryption stripping; damaged-file repair via rasterization |
| `PdfSharpCore` | PDF **document structure** read/write (open, merge/split, page ops, burn annotations on save) |
| `PdfPig` (`UglyToad.PdfPig`) | **Text extraction** for search |
| `System.Text.Json` | Settings/signature serialization |
| `Costura.Fody` / `Fody` | Single-EXE bundling |
| `PolySharp` | Modern C# polyfills for net48 |

### Project layout
```
Scalpel.sln
├─ Scalpel.csproj           WinExe, the app
│  ├─ App.xaml(.cs)           Entry point + installer + crash handling + settings/temp lifecycle
│  ├─ MainWindow.xaml(.cs)    The monolith — almost all UI/editing logic (~9.2k LOC code-behind)
│  ├─ PrintPreviewWindow.cs   Custom print dialog with preview
│  ├─ PageThumbnailVm.cs      Sidebar page-list view model
│  ├─ CrashReporter.cs        Structured crash logs + status ring buffer
│  ├─ BuildInfo.cs            pdfium.dll expected hash (written by release.ps1)
│  ├─ Models/EditingTypes.cs  Annotation model + EditTool enum
│  ├─ Services/
│  │   ├─ SearchService.cs    PdfPig full-text search
│  │   ├─ SignatureStore.cs   Saved signatures (JSON in AppData)
│  │   ├─ ThemeManager.cs     Theme/accent dictionaries + DWM title bar
│  │   ├─ ThemeMigration.cs   Legacy theme name migration (Blood/Greed/Cyanotic → two-axis)
│  │   └─ LocaleManager.cs    String dictionaries
│  ├─ Themes/*.xaml           3 base theme + Accents/ overlay ResourceDictionaries
│  └─ Strings/*.xaml          Per-locale string ResourceDictionaries
├─ Scalpel.Tests/           xUnit tests (link source files directly)
├─ build/bundle-source.ps1    GPL3 source-zip bundler (runs after Publish)
└─ release.ps1                Build → sign → verify → hash → release pipeline
```

### Key architectural decisions

**1. The MainWindow monolith.** `MainWindow.xaml.cs` (~9,200 lines, 440 KB) holds nearly the entire application: open/save with all fallbacks, every editing tool, rendering for all four view modes, search, form filling, signing, cropping, page operations, links, and outlines. Representative method families: `RenderPage`/`RenderContinuousPages`/`RenderAdditionalPages`, `Save_Click`/`SaveInPlace`/`SaveFlattened_Click`/`SaveTempAndReload`, `RenderFormFields`/`GetPageFormFields`/`GenerateTextFieldAppearance`, `Merge_Click`/`Split_Click`, `LoadOutlines`/`RenderPageLinks`/`RewriteNamedDestLinks`, `StartCropDraw`/`ApplyCrop`. Expect to work here for most behavior changes.

**2. Three PDF libraries, three jobs.** Rendering (Docnet/PDFium), structure (PdfSharpCore), and text (PdfPig) are intentionally separated. A critical constraint: **PdfSharpCore can read encrypted PDFs but cannot re-save them once modified**, so encrypted files are decrypted to a temp copy via PDFium (`FPDF_SaveWithVersion` with `FPDF_REMOVE_SECURITY`) before editing.

**3. Layered open-with-fallback chain.** Real-world PDFs are frequently malformed, so `Load` tries, in order: `PdfReader.Open` Modify → ReadOnly → Import-copy (lenient page copy into a fresh doc) → **PDFium rasterize-and-rebuild** (`RepairViaDocnetRasterize`, which renders each page to a bitmap and reconstructs a clean PdfSharpCore document). This is why files that fail in PdfSharpCore but open in browsers still work. Network/UNC files are copied to local temp first.

**4. Annotations are overlays, burned on save.** `Models/EditingTypes.cs` defines `PageAnnotation` subtypes (`TextAnnotation`, `InkAnnotation`, `HighlightAnnotation`, `TextEditAnnotation`, `SignatureAnnotation`, `ImageAnnotation`; the last two share `PlacedAnnotation` position/scale). While editing they are live WPF canvas elements; only on save are they drawn into the PDF (`DrawAnnotationsOnDocument`). `EditTool` enum drives tool state.

**5. Hot-swappable theme/locale via merged dictionaries.** `Application.Current.Resources.MergedDictionaries` is a fixed two-slot array: **index 0 = theme**, **index 1 = strings**. `ThemeManager` updates the theme dict **in place per key** (not structural add/remove) specifically to avoid a synchronous `ResourcesChanged` firing `FindResource` before the new dict is settled (`ResourceReferenceKeyNotFoundException`). It also sets the native dark title bar through `DwmSetWindowAttribute` P/Invoke, and force-refreshes icon colors. Adding a language = add `Strings/<locale>.xaml`, a `Locale` enum case, and a `pack://` URI case in `LocaleManager`.

**6. App.xaml.cs is also the installer.** Beyond being the entry point, it: registers three unhandled-exception sinks (Dispatcher, AppDomain, unobserved Task — note `AccessViolationException` is intentionally **not** caught on net48), handles the `/uninstall` flag from Add/Remove Programs, performs per-user self-install to `%LOCALAPPDATA%\Programs\Scalpel` with `.pdf` ProgId registration and Start-Menu/Desktop shortcuts (no UAC), and verifies pdfium integrity at startup.

**7. pdfium integrity gate.** `BuildInfo.PdfiumSha256` holds the expected hash. At startup `CheckPdfiumIntegrity()` decompresses the Costura-embedded pdfium resource and compares; a mismatch aborts the process (`Shutdown(2)`). All-zeros (`PdfiumSha256Disabled`) or a non-Costura dev build skips the check. `release.ps1` writes the real hash before each signed build.

**8. Defensive I/O everywhere.** Untrusted/malformed PDFs mean parsing and file I/O are wrapped in `try { } catch { }` that swallow and fall back rather than surfacing exceptions mid-edit. Follow this pattern when extending file handling.

### Persistence map
| Data | Location |
|---|---|
| Settings (theme, locale, view mode, window size, zoom, fit mode) | Registry `HKCU\Software\Scalpel\Settings` |
| Install / file-handler state | Registry `HKCU\Software\Scalpel` + standard Uninstall key |
| Saved signatures | `%LOCALAPPDATA%\Scalpel\signatures.json` |
| Crash logs (rolling, 20 MB cap) | `%LOCALAPPDATA%\Scalpel\Logs\crash_*.log` |
| Session temp PDFs | `scalpel_*.pdf`, swept on startup/exit |

---

## 4. Building, testing, releasing

```powershell
dotnet build                      # debug
dotnet publish -c Release         # single-file EXE -> bin/Release/net48/publish/ + GPL3 src zip
dotnet test                       # xUnit tests
dotnet test --filter "FullyQualifiedName~SearchService"   # a single test class
```

- **Publish** triggers the `BundleSource` MSBuild target (`build/bundle-source.ps1`) → `Scalpel-<version>-src.zip` for GPL3 corresponding source.
- **Tests** (`Scalpel.Tests`, xUnit) link the source files directly into the test project (`<Compile Include="..\Services\..">`) rather than referencing the WinExe. Moving a tested file means updating those link paths. Covered today: `SearchService`, `SignatureStore`.
- **`release.ps1`** is the production pipeline: locate/pre-hash `pdfium.dll` → write `BuildInfo.cs` → build (Release/net48/x64) → Authenticode sign (SimplySign/signtool) → **`signtool verify /pa` gate that aborts the release on failure** → SHA256 → summary. Not for everyday dev.
- **Distribution automation:** `.github/workflows/chocolatey-release.yml` and `winget-release.yml`; packaging metadata under `choco/`.

---

## 5. Conventions for contributors

- Match the existing modern-C# style (collection expressions, target-typed `new`, switch expressions) — the polyfills make them available on net48.
- Keep the open/save fallback chain intact when touching file loading; it's load-bearing for malformed-PDF tolerance.
- Preserve the in-place per-key theme dictionary update and the two-slot merged-dictionary ordering.
- Wrap untrusted I/O in swallowing try/catch with sane fallbacks, consistent with the rest of the code.
- New UI strings go through the localization dictionaries (`Loc(...)` lookups), not hard-coded literals.
