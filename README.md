# Scalpel — a local-only PDF editor for Windows

View, annotate, edit, merge, split, sign, fill forms, OCR, redact, and protect PDFs — without an Adobe subscription, an account, or a single network call. One self-contained **~6 MB EXE**. Install it or run it portable. **No telemetry, no phone-home.** GPLv3.

> The PDF equivalent of Notepad.

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Get%20it-2C3A4C?logo=microsoft)](https://apps.microsoft.com/detail/9n9hn8xw4lf3)
[![Website](https://img.shields.io/badge/website-scalpel--pdf.netlify.app-E11D38)](https://scalpel-pdf.netlify.app)
[![License: GPLv3](https://img.shields.io/badge/license-GPLv3-blue)](LICENSE)
![Windows 10/11 x64](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D6?logo=windows)

**Website:** <https://scalpel-pdf.netlify.app> · **Microsoft Store:** <https://apps.microsoft.com/detail/9n9hn8xw4lf3>

## Why this exists

Acrobat is bloated, hijacks file associations, wants a subscription for basics, and phones home constantly. Most "free" alternatives are ad-riddled, cloud-based, or the same PDF engine rebranded three times over.

Scalpel is the opposite: **everything runs on your machine.** Files are opened, edited, and saved locally — nothing is uploaded, there's no account, and there's no telemetry. Even OCR runs offline on your CPU.

## Screenshots

The "Clinical" ribbon interface — organized into **View · Edit · Pages · Sign** tabs, in light and dark.

| | |
|---|---|
| ![View — Light](store-assets/screenshots/01-view-light.png) | ![Edit — Dark](store-assets/screenshots/02-edit-dark.png) |
| **View** (Light / Clinical) | **Edit** (Dark) |
| ![Pages — Light](store-assets/screenshots/03-pages-light.png) | ![Grid — Green accent](store-assets/screenshots/06-grid-green.png) |
| **Pages** organize | **Grid** view (Green accent) |

## What it does

**Edit & annotate**
- Inline text editing with font matching against the original document; right-to-left scripts (Hebrew/Arabic) shape and read correctly
- Text boxes, freehand ink, and highlight overlays with adjustable color, size, and opacity
- Insert images as resizable annotations; crop pages with drag handles — all burned cleanly into the PDF on save

**Pages**
- Merge multiple PDFs, split out selected pages, drag-and-drop reordering
- Right-click sidebar: insert blank page, rotate, move, extract, or delete — on multi-page selections

**Sign & forms**
- Draw and save reusable signatures, or import a PNG/JPG/BMP and click to place
- Fill interactive PDF forms (text fields, checkboxes, radio buttons) and save back to the PDF

**Tools menu — document operations, run locally**
- **Page & Bates numbering** with corner headers/footers
- **Compression** (Low / Medium / High image down-sampling)
- **OCR** — make scans searchable with a bundled, offline Tesseract engine (portable builds fetch language data once, on demand)
- **Redaction** — permanently remove content (pixels gone, not just hidden)
- **Password protection** — encrypt with a password and set viewing/editing permissions
- **Metadata removal** — strip author, timestamps, and hidden data before sharing

**View & navigate**
- Single-page, continuous scroll, two-page, and grid view modes (persisted across sessions)
- High-quality PDFium rendering, with damaged-file recovery
- Outline/bookmark navigation, clickable links and cross-references, full-text search with highlighting, zoom presets with scroll-wheel sync, page-jump box, keyboard shortcuts (Ctrl+?)

**Output**
- Print with annotations flattened in
- Save a flattened (uneditable) copy
- Open password-protected PDFs (prompts instead of erroring)

**Themes & languages**
- Three base themes — **Light**, **Dark**, **High Contrast** — each with **Amber / Red / Green / Cyan** accents. The default is the **Light + Red "Clinical"** look. Switch live in Settings.
- **9 interface languages:** English, Spanish, Traditional & Simplified Chinese, Bengali, Turkish, Hebrew, Arabic, and Russian — with a fully mirrored **right-to-left** UI for Hebrew and Arabic. Contribute one via [`Strings/TRANSLATING.md`](Strings/TRANSLATING.md).

**Privacy & updates**
- 100% on-device. No account, no telemetry, no phone-home.
- Optional, opt-in update notifications (off until you enable them; checks a single static file, sends nothing about you).

## Download

- **Microsoft Store** (auto-updating, sandboxed): <https://apps.microsoft.com/detail/9n9hn8xw4lf3>
- **Website** (portable + installer): <https://scalpel-pdf.netlify.app>
- **Direct EXE** (latest release): <https://github.com/blakazulu/ScalpelPDF/releases/latest>

The portable EXE and the installer are the **same file** — run it in place to stay portable, or click Install and it sets itself up per-user in `%LOCALAPPDATA%` (no UAC), registers as the `.pdf` handler, adds shortcuts, and uninstalls cleanly via Add/Remove Programs. Web-downloaded builds are unsigned, so Windows SmartScreen may warn on first run (More info → Run anyway); the Store build is fully managed by Windows.

## Requirements

- Windows 10 or 11 (**x64**)
- No runtime install — everything is inside the EXE (targets **.NET Framework 4.8**, which ships with every supported Windows release)

## Build from source

```powershell
git clone https://github.com/blakazulu/ScalpelPDF.git
cd ScalpelPDF
dotnet publish -c Release
```

Output lands in `bin/Release/net48/publish/` as a single Costura-bundled `Scalpel.exe`, plus a versioned `Scalpel-<version>-src.zip` (GPLv3 corresponding source).

- Targets **.NET Framework 4.8** but **requires the .NET 8 SDK or later to build**. Output is x64.
- `dotnet test` runs the xUnit suite (`Scalpel.Tests`).
- `release.ps1` is the full signed-release pipeline (build → Authenticode sign → verify → hash). Ordinary dev builds don't need it.

### Microsoft Store / MSIX package

The same `Scalpel.exe` can be wrapped into an MSIX. When run from a package, the app detects MSIX and disables its self-installer — the OS/package owns install, uninstall, and the `.pdf` association. Build a local sideload test package with:

```powershell
pwsh -File packaging\build-msix.ps1 -SelfSign
```

See [`docs/STORE-PUBLISHING.md`](docs/STORE-PUBLISHING.md) for sideload testing and Store submission (including the GPLv3-on-the-Store licensing note).

## Documentation

- [`docs/OVERVIEW.md`](docs/OVERVIEW.md) — full architecture and what-it-does reference
- [`docs/system-design.md`](docs/system-design.md) — the "Clinical" ribbon design system (layout, themes, components, brand assets)
- [`docs/UI-REFERENCE.md`](docs/UI-REFERENCE.md) — every control mapped to its handler
- [`docs/STORE-PUBLISHING.md`](docs/STORE-PUBLISHING.md) — MSIX build + Store submission

## Changelog

See [CHANGELOG.md](CHANGELOG.md), or the in-app **What's New** popup.

## License

GPLv3. See [LICENSE](LICENSE). If you fork, modify, or redistribute Scalpel, your version must also be released under GPLv3 with source available. No exceptions for commercial rebrands.
