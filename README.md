# KillerPDF

[![oosmetrics](https://api.oosmetrics.com/api/v1/badge/achievement/bf0a2893-0ce5-4fc8-a6cc-084ad0722ed2.svg)](https://oosmetrics.com/repo/SteveTheKiller/KillerPDF)
[![oosmetrics](https://api.oosmetrics.com/api/v1/badge/achievement/dd592456-00ad-445d-b97b-21e44ee4b44e.svg)](https://oosmetrics.com/repo/SteveTheKiller/KillerPDF)

PDF editor for field techs. View, annotate, merge, split, edit text, draw, sign, print, flatten, and open password-protected PDFs without an Adobe subscription or a phone-home. Install or run portable. Single Windows EXE, ~6 MB zipped, no runtime install required.

Landing page is hosted at [pdf.killertools.net](https://pdf.killertools.net)

## Why this exists

I hate Adobe. Acrobat is bloated, tries to hijack file associations, wants a subscription to do basic things, and phones home constantly. Most of the "free" alternatives are either ad-riddled, cloud-based, or rebrands of the same PDF engine sold under three different names.

KillerPDF is what I wanted: local-only, portable, no account, no telemetry. The PDF equivalent of Notepad.

## Features

- High-quality rendering via PDFium
- Merge multiple PDFs and split out selected pages, drag-and-drop page reordering
- Inline text editing with font matching against the original document
- Text boxes, freehand drawing, and highlight overlays with adjustable color, size, and opacity
- Draw and save reusable signatures or import a PNG/JPG/BMP image as a signature, click to place anywhere on a page
- Insert images onto any page as resizable annotations - drag the corner handle to scale, burned into the PDF on save
- Crop tool with corner drag handles; Enter to apply, Escape to cancel, remove crop from one page or all pages
- Right-click sidebar: insert blank page, rotate CW/CCW, move up/down, extract, or delete - works on multi-page selections
- PDF form filling: text inputs, checkboxes, and radio buttons render as live controls - fill and save back to PDF
- PDF outline (bookmark) navigation: OUTLINES tab in the sidebar displays the bookmark tree; click any entry to jump to that page
- Clickable PDF links and internal cross-references, including TOC back-links
- Four view modes selectable in Settings: Single Page, Continuous scroll (all pages in one vertical strip), Two-Page (side-by-side), and Grid. Choice persists across sessions.
- Localized UI: English, Spanish, and Traditional Chinese included. Contribute a translation via `Strings/TRANSLATING.md`.
- Six color themes: Dark, Light, High Contrast, Blood, Greed, and Cyanotic. Switch live in Settings.
- Zoom preset dropdown with scroll-wheel sync; Fit to Width and Fit Page re-apply on window resize
- Page number jump box in the toolbar; type a page number and press Enter to navigate directly
- Keyboard-driven navigation with arrow keys and middle-mouse panning, plus a full shortcut overlay (Ctrl+?)
- Full-text search across the entire document with result highlighting, drag-select to copy text
- Print with annotations flattened into the output, print preview is rendered in print dialog.
- Save Flattened PDF: rasterizes every page at 150 DPI into a fully uneditable document
- Password-protected PDF support: prompts for password instead of erroring
- Self-installing EXE: installs per-user to %LOCALAPPDATA% (no UAC), registers as PDF file handler, adds Start Menu and optional Desktop shortcuts, uninstalls cleanly via Add/Remove Programs

## Screenshots

Six themes to choose from:

**Dark**
![KillerPDF - Dark theme](screenshots/6_Dark.png)

**Blood**
![KillerPDF - Blood theme](screenshots/1_Blood.png)

**Greed**
![KillerPDF - Greed theme](screenshots/2_Greed.png)

**Cyanotic**
![KillerPDF - Cyanotic theme](screenshots/3_Cyanotic.png)

**High Contrast**
![KillerPDF - High Contrast theme](screenshots/4_High_Contrast.png)

**Light**
![KillerPDF - Light theme](screenshots/5_Light.png)

## Requirements

- Windows 10 or 11 (x64)
- No runtime install. Everything needed is inside the EXE (targets .NET Framework 4.8, which ships with every supported Windows release).

## Download

```powershell
winget install killerpdf
```

- Prebuilt binary: <https://github.com/SteveTheKiller/KillerPDF/releases/latest/download/KillerPDF.exe>
- Source (GPL3 corresponding source for this release): <https://github.com/SteveTheKiller/KillerPDF/releases/download/v1.5.1/KillerPDF-1.5.1-src.zip>

## Build from source

```powershell
git clone https://github.com/SteveTheKiller/KillerPDF.git
cd KillerPDF
dotnet publish -c Release
```

Output lands in `bin/Release/net48/publish/`. The publish step produces a single Costura-bundled `KillerPDF.exe` plus a versioned `KillerPDF-<version>-src.zip` for GPL3 source distribution.

Requires the .NET 8 SDK or later to build (even though the output targets .NET Framework 4.8).

### Microsoft Store / MSIX package

The same `KillerPDF.exe` can be wrapped into an MSIX for the Microsoft Store. When run from a package, the app detects MSIX and disables its self-installer (the OS/package owns install, uninstall, and the `.pdf` association). Build a local test package with:

```powershell
pwsh -File packaging\build-msix.ps1 -SelfSign
```

See [`docs/STORE-PUBLISHING.md`](docs/STORE-PUBLISHING.md) for sideload testing and Store submission (including the GPLv3-on-the-Store licensing note).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

GPLv3. See [LICENSE](LICENSE). If you fork, modify, or redistribute KillerPDF, your version must also be released under GPLv3 with source available. No exceptions for commercial rebrands.
