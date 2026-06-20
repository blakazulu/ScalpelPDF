# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

KillerPDF is a local-only, portable Windows PDF editor (view, annotate, merge/split, edit text, sign, fill forms, print, flatten). It ships as a single self-installing ~6 MB EXE with no runtime install and no telemetry. GPLv3.

## Build / run / test

```powershell
dotnet build                      # debug build
dotnet publish -c Release         # single-file Costura EXE -> bin/Release/net48/publish/
dotnet test                       # run xUnit tests (KillerPDF.Tests)
dotnet test --filter "FullyQualifiedName~SearchService"   # single test class
```

- Targets **.NET Framework 4.8** (`net48`) but **requires the .NET 8 SDK or later to build**. Output is x64.
- `dotnet publish` also runs `build/bundle-source.ps1` (the `BundleSource` MSBuild target) to produce a GPL3 `KillerPDF-<version>-src.zip` alongside the EXE.
- `release.ps1` is the full release pipeline (build → Authenticode sign → `signtool verify /pa` gate → SHA256 → write `BuildInfo.cs` → summary). Don't run it for ordinary dev work; it expects signing certs/SimplySign.

## Architecture

WPF desktop app. There is no MVVM framework in play for the main window — it is a large code-behind file.

- **`MainWindow.xaml.cs` (~9200 lines, 440 KB) is the monolith.** Nearly all editing, rendering, navigation, search, form-filling, signing, cropping, and save logic lives in this one `partial class MainWindow`. When adding UI behavior, expect to work here. `MainWindow.xaml` is the matching ~90 KB layout.
- **`App.xaml.cs`** is the entry point AND the installer. It handles crash dialogs (3 unhandled-exception sinks), `/uninstall`, per-user self-install to `%LOCALAPPDATA%\Programs\KillerPDF` with `.pdf` file-handler registration (no UAC), the pdfium integrity check, settings, and temp-file lifecycle.

### Three PDF libraries, three distinct roles

Do not assume one library does everything — they are deliberately split:

- **Docnet.Core** (bundles `pdfium.dll`) — **rendering** pages to bitmaps, plus encryption stripping and damaged-file repair via rasterization (`RepairViaDocnetRasterize`). PDFium also recovers files PdfSharpCore's parser rejects.
- **PdfSharpCore** — reading/writing PDF document **structure**: opening (`PdfReader.Open`), merge/split, page ops, and burning annotations on save. Note: it can *read* encrypted PDFs but cannot *re-save* them once modified, so the code decrypts to a temp copy first.
- **PdfPig** (`UglyToad.PdfPig`) — **text extraction** for full-text search (`Services/SearchService.cs`). Also referenced by the test project.

The open path in `MainWindow` has layered fallbacks (Modify → ReadOnly → Import-copy → PDFium rasterize-rebuild) because real-world PDFs are frequently malformed; preserve this fallback chain when touching file loading.

### Annotations are overlays burned in on save

`Models/EditingTypes.cs` defines the annotation model (`TextAnnotation`, `InkAnnotation`, `HighlightAnnotation`, `TextEditAnnotation`, `SignatureAnnotation`, `ImageAnnotation`, all `PageAnnotation` subtypes; `EditTool` enum). Annotations are live WPF overlays while editing and are rasterized/drawn into the PDF only on save (text edits white-out the original bounds and redraw). `PageThumbnailVm.cs` backs the sidebar page list.

### Themes and localization are hot-swappable ResourceDictionaries

- `Services/ThemeManager.cs` — 6 themes (`Themes/*.xaml`). **Index 0** of `Application.Current.Resources.MergedDictionaries` is the theme dict; it is updated **in place per-key** (not by structural add/remove) to avoid `ResourceReferenceKeyNotFoundException` during live switches. Also sets the DWM dark title bar via P/Invoke.
- `Services/LocaleManager.cs` — strings live in `Strings/*.xaml` (en-US, es, zh-TW, zh-CN, bn, tr-TR). **Index 1** of the merged dictionaries is the strings dict. Adding a language = add a `Strings/<locale>.xaml`, a `Locale` enum entry, and a `pack://` URI case. See `Strings/TRANSLATING.md`.

Both managers persist the user's choice and restore it at startup (`Initialize()` is called from `App.OnStartup` before `MainWindow`).

### Persistence

- **Settings** → Windows registry under `HKCU\Software\KillerPDF\Settings` (`App.GetSetting` / `SetSetting`). Install/handler state lives under `HKCU\Software\KillerPDF` and the standard Uninstall key.
- **Saved signatures** → `%LOCALAPPDATA%\KillerPDF\signatures.json` (`Services/SignatureStore.cs`, `System.Text.Json`).
- **Temp files** → `killerpdf_*.pdf`, registered per-session and swept on startup/exit (`CleanupSessionTemps` / `CleanupStaleTemps`).

### pdfium integrity check

`BuildInfo.cs` holds the expected SHA256 of `pdfium.dll`. At startup `CheckPdfiumIntegrity()` decompresses the Costura-embedded pdfium resource and compares hashes; mismatch aborts the app. All-zeros (`PdfiumSha256Disabled`) disables the check for dev/`SkipSign` builds, and dev builds running loose from `bin/` (not Costura-bundled) skip it too. `release.ps1` writes the real hash.

## Conventions

- C# with `Nullable` enabled and `ImplicitUsings` enabled; `LangVersion=latest`. Collection expressions (`[]`), target-typed `new`, and `switch` expressions are used throughout — match that style.
- I/O and parsing are wrapped in defensive `try { } catch { }` that swallow and fall back (PDFs are untrusted/malformed); follow this pattern rather than letting exceptions reach the user mid-edit.
- Tests (`KillerPDF.Tests`) are xUnit and **link the source files directly** (`<Compile Include="..\Services\...">`) rather than referencing the WinExe project. If you move a tested file, update the `.csproj` link paths.
