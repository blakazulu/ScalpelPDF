# KillerPDF upstream analysis: 1.6.0 to 1.8.1

Scalpel was forked from KillerPDF at **v1.6.0** (2026-06-27). The reference dump under
`docs/github-origin/` (untracked, gitignored, excluded from compilation by
`<Compile Remove="docs\**" />` in `Scalpel.csproj`) was refreshed on 2026-08-30 to
**v1.8.1** (2026-08-29), the newest tag on <https://github.com/SteveTheKiller/KillerPDF>.

This document is a deep inventory of everything upstream fixed and added between those two
tags, so the Scalpel port program can pick from it deliberately. It is Windows-only by
design: macOS (#161), ARM64 (#270, still open), Wine/CrossOver notes, the marketing website
(`pdf-landing/`), and NuGet/Chocolatey/WinGet publishing plumbing are mentioned only where
they touch the Windows app.

Sources: upstream `CHANGELOG.md` (1.6.1 through 1.8.1), `engine/CHANGELOG.md`,
`engine/README.md`, the three ADRs under `engine/docs/architecture/`, `validation/RESULTS.md`
and `validation/PERFORMANCE.md`, the 591 commits in `v1.6.0..v1.8.1`, the GitHub issue and PR
list (#83 through #304), and the source tree itself.

---

## 1. Executive summary

| Metric | 1.6.0 | 1.8.1 |
|---|---|---|
| Target framework | `net48` (x64) | **`net10.0-windows`** (x64) |
| PDF structure library | PdfSharpCore 1.3.67 (+ PDFsharp 6.2.4 for signing) | **The KillerPDF.Engine** (in-repo, 84.6k lines, published on NuGet) |
| Renderer / text extraction | Docnet.Core (PDFium) / PdfPig 0.1.14 | unchanged / PdfPig 0.1.15 |
| Distribution | one Costura single-file EXE, self-installs | **two packages**: framework-dependent installer + self-contained portable, both via a `KillerLauncher` stub with a verified `payload.manifest` |
| App source (non-engine) | ~66 `.cs` files at repo root | 54.5k lines of C# + 22.7k lines of XAML, organized into `Shell/`, `Controls/`, `Controls/Viewer/`, `Features/`, `Services/`, `Models/`, `Packaging/` |
| Interface languages | 9 | **15** (+ ja-JP, cs-CZ, pl-PL, hu-HU, it-IT, kk-KZ, ru-RU) |
| OCR languages | 8 | **13** (every UI language has a matching Tesseract model) |
| Themes | 6 | **13** (+ 98SE, Ectoplasm, Decay, Mourning, Sepulchre, Delirium, Malaise) with accent overlays for Dark/Light/Black/98SE (33 looks) |
| Application unit tests | 2 files | 40 files, 195 `[Fact]`/`[Theory]` |
| Engine unit tests | none | 65 files, 1,072 `[Fact]`/`[Theory]` (README claims 1,436 test cases incl. theory rows) |
| Commits between tags | | 591 |
| Files changed | | 662 (507 added, 36 deleted, 26 renamed, 65 modified) |
| Issues referenced in changelog | | ~150 distinct `#NNN` |

Release cadence: 1.6.1 (07-01), 1.6.2 (07-11), 1.6.3 (07-12), 1.6.4 (07-17), 1.6.5 (07-22),
1.6.6 (07-23), 1.7.0 (08-01), 1.7.1 (08-04), 1.7.2 (08-15), 1.7.3 (08-15), 1.7.4 (08-21),
1.7.5 (08-22), 1.8.0 (08-28), 1.8.1 (08-29). Fourteen releases in nine weeks.

The three big structural moves, each recorded in an ADR:

1. **ADR-001 (08-22):** build an independent, UI-free PDF 2.0 document engine (`KillerPdf.Engine`, .NET 10) instead of extending vendored PdfSharpCore.
2. **ADR-002 (08-24):** retarget the WPF shell from `net48` to `net10.0-windows`, reference the engine directly, and replace PdfSharpCore in tested vertical slices (completed in 1.8.0-alpha.1; PdfSharpCore and PDFsharp fully removed).
3. **ADR-003 (08-25):** split distribution into a ~10 MB framework-dependent installer (requires .NET 10 Desktop Runtime) and a ~69 MB self-contained portable EXE.

---

## 2. Platform and architecture changes

### 2.1 .NET Framework 4.8 to .NET 10

- `KillerPDF.csproj`: `net48` to `net10.0-windows`, `AllowUnsafeBlocks` (for `LibraryImport`),
  `GenerateAssemblyVersionAttribute=false`, a `ReleaseDate` MSBuild property baked into the
  assembly as `AssemblyMetadata` and shown on the About card (release.ps1 enforces it matches the
  changelog date).
- Dropped: `PolySharp`, `Microsoft.NETFramework.ReferenceAssemblies`, `System.Text.Json` package
  (now in-box), the `System.Printing`/`ReachFramework`/`System.Net.Http`/`System.IO.Compression`
  framework references. Costura.Fody 5.7 to 6.2, Fody 6.8 to 6.9.3, CommunityToolkit.Mvvm 8.4.0 to
  8.4.2, PdfPig 0.1.14 to 0.1.15. Added `SixLabors.Fonts 1.0.0-beta17`.
- `Services/RuntimeEncodingBootstrap.cs`: registers `CodePagesEncodingProvider` at startup
  (legacy code pages are not in-box on modern .NET).
- `Services/PdfiumInterop.cs` (528 lines): all direct PDFium P/Invoke consolidated in one file;
  document paths and passwords now use explicit UTF-8 marshalling instead of the ANSI code page
  (1.8.0).
- A pre-1.8 fix that matters for net48 forks: PR #218 opts out of PolySharp's generated
  `EmbeddedAttribute` to avoid the `CS8336` attribute collision (1.7.4).

### 2.2 The KillerPDF.Engine (PdfSharpCore removed)

`engine/KillerPdf.Engine` targets plain `net10.0`, has one dependency
(`System.Security.Cryptography.Pkcs`), treats warnings as errors, generates XML docs, and is
published as the `KillerPdf.Engine` NuGet package (GPL-3.0-only). It is a parser/writer/editor,
**not** a renderer: PDFium still renders, PdfPig still extracts text.

Subsystems (source line counts at 1.8.1):

| Folder | Files | Lines | Role |
|---|---:|---:|---|
| `Authoring/` | 85 | 12,669 | `PdfDocumentBuilder`, pages, content streams, graphics state, fonts, images, color spaces (ICC, Lab, Indexed, spot, calibrated), shadings/gradients, tiling patterns, transparency/blend modes, Form XObjects, annotations, AcroForm fields, bookmarks, destinations, page labels, optional content, attachments, PDF/A-4/-4e/-4f and PDF/UA-2 safeguards |
| `Editing/` | 3 | 21,747 | byte-preserving incremental editing: pages (insert/import/delete/reorder/rotate/crop/resize), annotations, links, bookmarks, forms, metadata, attachments, optional content, typed page overlays, save sanitizer |
| `Signing/` | 10 | 2,638 | detached CMS/CAdES signing, certification permissions, field locks, seed constraints, visible appearances, signature discovery, cryptographic verification, signed-revision analysis |
| `Security/` | 5 | 1,857 | Standard Security handler revisions 2 through 6: RC4, AES-128, AES-256, crypt filters, permissions, authenticated import/rewrite |
| `Writing/` | 8 | 2,528 | deterministic full rewrite, incremental update writer, write policy |
| `Documents/` | 12 | 1,892 | `PdfDocument.Open`, page tree, name/number trees, readers for bookmarks, links, form widgets, page info |
| `CrossReference/` | 5 | 1,029 | classic xref tables, xref streams, object streams, trailers, incremental revisions, linearized files |
| `Fonts/` | 2 | 1,018 | TrueType/CFF embedding, subsetting, ToUnicode |
| `Filters/`, `Syntax/`, `Objects/`, `Parsing/`, `Diagnostics/`, `Validation/` | 21 | ~2,400 | tokenizer, object model, stream filters, bounded parsing with implementation limits, structural diagnostics with file offsets |

Design principles stated in `engine/README.md`: preserve bytes where an incremental revision
can express the change; fail closed when structure cannot be interpreted safely; deterministic
output; explicit limits before allocating unbounded structures; typed public APIs; conformance
is validator-backed (veraPDF/qpdf), never inferred from the `%PDF-x.y` header.

Application-side integration lives in `Services/PdfEngineIntegration.cs` (1,098 lines),
`Services/PdfEngineBurn.cs` (422 lines, the annotation/stamp burn-in as isolated typed
Form XObject overlays), `Services/PdfEngineDocumentSession.cs` (an immutable engine view of
the open document shared by sidebar, links, forms, crop and viewport geometry) and
`Services/PdfWorkingDocument.cs` (engine-validated serialized working state; the mutable
PdfSharpCore `PdfDocument` handle is gone).

Consequences visible to users (1.8.0 changelog, "Changed"):

- Every save path (save, export, flatten, print, sign, searchable OCR) now goes through the
  engine while preserving Unicode text, transparency, rotations, form values, link targets,
  signatures and annotation appearances.
- Page insert/delete/duplicate/extract/reorder/merge/Transform-replace preserve full page and
  catalog structure: forms, tags, bookmarks, named destinations, layers, attachments, inherited
  page state.
- Undo is recorded automatically for every successful serialized document mutation (page ops,
  cross-pane copy, forms, links, metadata, bookmarks, crop, rotation), with annotations and page
  rotations restored alongside the PDF; history budget is 20 actions / 256 MB per document
  (`Services/UndoHistoryBudget.cs`, #266).
- Difficult PDFs: tolerant handling for encrypted files, damaged structures, unusual page sizes,
  last-resort raster recovery; qpdf-compatible stream lengths accepted; oversized xref/object
  streams, reused tree nodes, malformed shared form fields and repeated structure elements are
  rejected safely.
- A 1.7.x compatibility bug surfaced by the engine: 1.7.x rejected any header above PDF 1.9, so
  engine-authored PDF 2.0 files could not be opened by the old build (#255).

Performance (`validation/PERFORMANCE.md`): the 2,236-file batch-resave benchmark went from a
median 16.17 s (1.7.5, PdfSharpCore) to 10.01 s (1.8.0), a 38% reduction; 1.8.1 is within noise
of 1.8.0.

### 2.3 Viewer extracted into a control; split panes

- 1.7.0 moved ~8,700 lines (render pipeline, zoom, annotations, text editing, crop, forms, links,
  selection) out of `MainWindow` into a self-contained `Controls/Viewer/PdfViewer` control
  (`PdfViewer.*.cs` partials: `Annotations`, `Api`, `Bridge`, `Bridge2`, `Comparison`, `Crop`,
  `FormAuthoring`, `Forms`, `Links`, `Measurement`, `PageDrop`, `PageSelection`, `Selection`,
  `Tabs`, `TabsApi`, `TabStrip`, `TextEditing`, `Viewport`, `Zoom`). `Models/ViewerState.cs`
  holds per-pane zoom/render/gesture state and `CurrentPage`.
- The window talks to the viewer through `Features/Viewer/IViewerHost` and
  `Features/IShellServices`; About/CLI/OCR/Search became controllers behind host interfaces
  (`Features/About`, `Features/Cli`, `Features/Ocr`, `Features/Search`).
- Repo layout was normalized to the "Killer Tools family" shape: window partials under
  `Shell/`, dialogs under `Controls/`, models under `Models/`, document logic under `Services/`.
  Root now holds only `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `AssemblyInfo.cs`.
- 1.7.2 completed the refactor so split panes keep independent documents, tabs, pages, tools,
  selections and sidebar positions (`Shell/SplitPane.cs`, 680 lines; `Shell/PaneDrag.cs`).

### 2.4 Packaging: launcher + payload, installer vs portable

Timeline:

- **1.7.4 (#189):** one portable download that installs as a normal multi-file application.
  The public EXE is a small `KillerLauncher` (WinForms, `Packaging/KillerLauncher/`) carrying a
  compressed, SHA-256-manifested payload (`payload.zip` + `payload.manifest`). Installed shortcuts
  launch the inner `KillerPDF.App.exe` directly, skipping Costura extraction: measured first
  startup ~40% faster, package ~34% smaller. Unsigned local dev packages can exercise the full
  install path; public release launchers keep a non-bypassable Authenticode requirement.
- **1.8.0 (ADR-003):** two artifacts built from one commit by `build/build-packages.ps1` ->
  `build/build-portable.ps1 -PackageKind Portable|Installer`. `KillerPDF.exe` (installer,
  framework-dependent, compact Dark-theme wizard: identity rail, runtime detection, account
  scope, shortcut selection, verified install, elevation, rollback, launch) and
  `KillerPDF-Portable.exe` (self-contained, offline, `PORTABLE` upgrade badge instead of an
  Install button). `KillerPayloadBuild=true` builds the inner app with Fody disabled as
  `KillerPDF.App`.
- `--verify` / `/verify` validate every installed file against `payload.manifest` on demand
  (`Services/PayloadIntegrityVerifier.cs`); files absent from the manifest are rejected; the
  launcher's own `.killerpdf-portable` marker is whitelisted (#236, #279). This replaced the
  dead Costura-only pdfium hash check that had silently not run since 1.7.4 (#236).
- `/silent` machine-wide install for winget/Chocolatey/RMM (1.7.0); refuses with exit code 10
  before writing anything when the .NET 10 Desktop Runtime is absent (#275,
  `Packaging/KillerLauncher/InstallerPrerequisitePolicy.cs`).
- Install-scope guard (1.7.4): per-user and all-users installs cannot coexist; dual installs
  are detected at startup with an offer to remove the inactive copy; converting to all-users
  removes the per-user copy; machine-wide uninstall requests elevation instead of reporting
  success after a permission failure. 1.8.1 (#285) closes legacy installs safely before
  upgrading, keeps installer failures inside themed notices, and repairs duplicate scopes
  without touching settings or PDFs.
- Portable cleanup validates process identity + start time instead of trusting a reusable PID
  (`PortableProcessIdentity.cs`, `PortableDirectoryCleanup.cs`); extraction markers keep a
  directory only while their recorded child still runs from it.
- All-users installer handoff completes once and leaves the final relaunch to the portable UI
  (#238); portable packaging runs a disposable install smoke test.
- `build/measure-startup.ps1` measures cold start; `Services/StartupTrace.cs` is the
  never-fails diagnostics hook.

### 2.5 Registration and protocol handler

- Machine-wide installs register the PDF handler under HKLM for every account (#176, 1.7.1) and
  the `killerpdf://` handler for all users so it appears in Default apps > link types (#183,
  1.7.2). `Services/ProtocolRegistrar.cs`.
- `killerpdf://open?url=...` (1.7.1) hands a public HTTPS PDF to a closed or running instance;
  downloads are size-limited and must begin with `%PDF`. Registration refreshes when the EXE
  moves. Foundation for a planned Chrome extension.
- Fixes: stale per-user protocol key shadowing the machine handler is cleared (#246, PR #258);
  a portable run no longer steals the handler from an install and refuses to shadow a valid
  installed handler (#267); a `killerpdf:` launch that starts the app no longer crashes
  (#276, PR #277); refusals say why (PR #278).
- `Shell/ExternalOpen.cs`: a path forwarded from a second launch before the panes exist is
  replayed from `Loaded` (fixes the startup NRE when opening from Explorer while the window
  is still constructing, #202).

---

## 3. Features added (by area)

### 3.1 Viewing and navigation

| Feature | Version | Refs |
|---|---|---|
| Split pane (F10): two documents side by side, each a card with its own tab strip; focus ring; per-side resize handles with minimum widths; drag a tab across panes; even split on maximized/snapped windows | 1.7.0 | |
| Per-pane night mode; moon icon follows pane focus | 1.7.2 | |
| Night mode keeps pictures in real colors; Shift+N / moon right-click for full inversion; display-only (save/print/export/OCR/thumbnails keep true colors) | 1.6.5 (invert, Ctrl+I), 1.7.0 (image carve-out) | #135 |
| Book layout for Two-Page view (cover alone, facing pairs) | 1.7.2 | #193 |
| Current-page corner badge that slides away when the view settles (replaces cursor tooltip); fires on grid scroll and names the visible span; shadow (flat on 98SE) | 1.7.2, 1.7.4, 1.7.5 | #197, #151 |
| Jump history: Alt+Left/Right and mouse back/forward retrace bookmark, link, jump-box, Home/End jumps | 1.6.4 | |
| Home/End first/last page; Ctrl+1/2/3 actual size / fit width / fit page; Menu key or Shift+F10 opens context menu | 1.6.4 | |
| Page Up/Down navigate regardless of focus; Up/Down arrows scroll like the wheel and flip at edges | 1.6.2 | #117 |
| Smooth Ctrl+wheel zoom (constant 10% per notch, instant scale, one crisp re-render at rest; touchpads glide) | 1.6.2 | |
| Shift+wheel horizontal scroll (same path as tilt wheel); touchpad/tilt horizontal pan | 1.7.5, 1.7.2 | #209, #196 |
| Wheel scrolling honors the Windows Mouse Properties line count | 1.8.1 | #301 |
| Touch: one-finger pan and pinch-to-zoom around a focal point in every layout (`Services/PinchZoomMath.cs`) | 1.8.0 | #271 |
| Select tool shows an I-beam only over selectable text, hand over links; open/closed-hand drag cursors across bars, find bar, signature popup, panning, stamps, Transform handles (`Resources/open_hand.cur`, `closed_hand.cur`, `Controls/DragCursors.cs`) | 1.8.0 | #221 |
| Remembered fit mode (Fit Width / Fit Page) for later opens; manual zoom restored on reopen; a raw zoom saved for a different window size is not reapplied | 1.7.1, 1.7.2 | #201 |
| Column-aware text selection (dragging down one column of a two-column page; copy in column order) | 1.7.2 | #185 |
| Flowing, browser-style text selection across lines/paragraphs/pages; Ctrl+A shows per-line selection | 1.6.5 | #127 |
| View-mode rail button with flyout, wheel-cycle, F5-F8 direct | 1.7.0 | |
| Hide toolbar (Alt+M / toolbar right-click); full screen no longer stays over other apps when switched away | 1.7.4 | #215 |
| Click the status line (or Shift+F4) to show the open file's size | 1.7.2 | |
| Outline sidebar: top level open, deeper folded, state sticks; expand/collapse-all toggle | 1.7.2, 1.8.1 | |

### 3.2 Dual-pane PDF comparison (1.8.0, #160)

`Shell/PdfComparison.cs` (481 lines), `Controls/Viewer/PdfViewer.Comparison.cs`,
`Services/PdfPageDifference.cs`. Opens a second document beside the original, synchronizes page
navigation, zoom and scroll, highlights visual difference regions, reports changed-page
percentages and dimension mismatches, and identifies pages present in only one document.

### 3.3 Measurement tool (1.8.0, #162)

`Controls/Viewer/PdfViewer.Measurement.cs`, `Services/MeasurementCalculator.cs`. Temporary
ruler on any rendered page; distance in inches, millimeters and PDF points plus rotated page
size. 1.8.1 keeps rulers/readouts legible at fitted and multi-page zoom levels (#292) and
themes the readout.

### 3.4 Pages panel and page organization

| Feature | Version | Refs |
|---|---|---|
| Bookmark editing in the Outline panel: add (inline named), rename (F2), child, reorder, retarget, delete, multi-select, delete-all, undo; hidden on read-only files | 1.6.4 | #133 |
| Drop PDFs/images onto the Pages sidebar to append | 1.7.2 | #172 |
| Dropping a damaged PDF on the Pages panel offers the same repair as Open | 1.7.4 | #203 |
| Export page(s) as image from the Pages right-click menu (multi-selection) | 1.7.4 | #207 |
| Multi-selected thumbnails drag as one ordered block; PDFs/images drop at an exact position or merge after a selected page | 1.8.0 | #233 |
| Theme-colored insertion line at the exact before/after drop position | 1.8.0 | #283 |
| Cross-document page copy in split view: drag thumbnails onto the other document or its Pages panel; the panel follows the document under the cursor; translucent preview with count badge (`PdfViewer.PageDrop.cs`, `Services/PageDragCursor.cs`) | 1.8.0 | #213 |
| Ctrl+A selects every page; Delete removes selected pages | 1.8.1 | #289, #296 |
| Sidebar collapsed when no document is open; smooth slide animation; edge fades on all themes except 98SE | 1.6.6, 1.6.5, 1.7.4 | |
| Sidebar toggle moved Ctrl+B to F9, side flip Shift+F9 (frees Ctrl+B for bold) | 1.7.5 | |
| Duplicated page stays selected after the rebuilt tree loads | 1.8.0 | |

### 3.5 Annotation and editing tools

| Feature | Version | Refs |
|---|---|---|
| Shapes tool: rectangle, ellipse, free-form polygon (click-to-place, double-click/first-point close, Backspace removes last point), optional fill, shares draw bar color/size/opacity (`Shell/Shapes.cs`) | 1.6.5 | #127 |
| Flowing Highlight/Strikethrough/Underline that hug each line; one grouped annotation per page; status hint on pages with no text layer | 1.6.5 | #127 |
| Saved highlights use the Multiply blend mode | 1.7.2 | #200 |
| Redo (Ctrl+Y / Ctrl+Shift+Z), per tab | 1.6.4 | |
| Ctrl+B/I/U bold/italic/underline while editing a text box (finally wired in 1.7.5) | 1.6.6 remap, 1.7.5 | |
| Letter spacing on text boxes with live preview, spacing-aware wrapping and matching PDF output (for pre-printed form boxes) | 1.8.0 | #232 |
| Text annotation toolbar two-row layout (font/size, text/fill color, text/fill opacity) | 1.7.5 | |
| Annotate bars reflow into single-row groups on narrow panes with an overflow chevron | 1.7.4 | |
| Tool hotkey remap: V select, 1 Text, 2 Highlight, 3 Line, 4 Shapes, 5 Draw, 6 Image, 7 Signature, 8 Crop, 9 Transform, 0 Stamp; toolbar reordered to match | 1.6.6 | |
| Esc steps down: cancel, then Select tool, then quit | 1.6.6 | |
| Screen eyedropper: OK no longer discarded by the nested capture modal; hover/armed states | 1.7.0 | |
| Fillable text-field authoring tool (F): create, select, move, resize, recolor (fill color), delete; localizable (`PdfViewer.FormAuthoring.cs`) | 1.8.0-beta.1, 1.8.1 | #295 |
| Clicking dotted/underscored blanks on flattened forms creates an empty text entry box over the detected field (`Services/TextEntryPlaceholder.cs`) | 1.8.0 | #273 |
| Double-click text edit keeps bold/italic from the embedded font name (`Helvetica-BoldOblique`) and maps PostScript names (ArialMT, TimesNewRomanPSMT) to Windows families (`Services/PdfFontStyle.cs`) | 1.7.1, 1.7.2 | #182, #187 |
| Non-Latin text survives save: reads `.ttc` collections, coverage-based fallback chain preferring Windows' own fallbacks, subset embedding, unsupported-character warning (`Services/FontCoverage.cs`, `Services/CmapCoverage.cs`) | 1.7.0 | #168 |
| Full RGB color picker OK button readable at rest; 98SE classic frame | 1.7.4 | #227 |

### 3.6 Transform tool

| Feature | Version | Refs |
|---|---|---|
| Perspective (trapezoid) correction with four draggable corner handles, composable with rotate/deskew/scale/flip (`Services/PerspectiveWarp.cs`) | 1.7.1 | #175 |
| LEVELS: black point, white point, midtone for pale scans | 1.7.2 | #174 |
| Collapsible sections (Rotate open by default) | 1.7.1 | |
| Page-by-page preview; apply to every selected page as one undoable batch | 1.8.0 | #204 |
| Page quality: grayscale, thresholded black-and-white, resample 72 to 600 DPI, selectable JPEG compression (`Services/PageQualityConverter.cs`) | 1.8.0 | #173 |
| Superseded page-image data removed so quality settings actually shrink the file | 1.8.1 | #287 |
| Batch progress dialog; rounded pane; 98SE scrollbar moved outward | 1.8.1 | #290, #291 |
| Commits an active text box before building the preview | 1.7.5 | |
| Rotating a landscape page by a few degrees no longer squashes it to portrait | 1.7.0 | #167 |

### 3.7 Forms

| Feature / fix | Version | Refs |
|---|---|---|
| Comb fields: typing capped at cell count; one character per printed box on save (`Services/CombFieldLayout.cs`) | 1.7.2 | #158 |
| Font-size stepper as an "inline flyout" pill on the document (`Controls/FlyoutPlacement.cs`) | 1.6.5 | |
| Complete appearance streams for filled fields: `/Length`, multiline layout, WinAnsi encoding (PR #180); invariant-culture numbers so comma-decimal locales get valid streams; fields no longer drawn twice in print/flatten/export | 1.7.1, 1.7.4 | #179, #180 |
| Viewer stops baking field appearances under the live overlays (no ghost text) | 1.7.2 | |
| Form-field overlays correct on non-A4 pages (parser read `/MediaBox` both representations, walks page-tree inheritance) | 1.6.6 | |
| Unicode form values embed a compatible font instead of rejecting the save; previously embedded subsets are reused so repeated edits do not grow the file ~435 KB per save | 1.8.0 | #244, #256 |
| Choice fields bind display option and export value explicitly; multi-select values load/display/edit/save completely; list-box rows bounded to the field rectangle; push-button appearances visible; mouse can pick from dropdown; lists take the wheel | 1.8.0 (PRs #249-#254) | #245 |
| Application shortcuts (Save, Find, Print, F1-F12, tab commands) work while a field or typewriter box has focus; typing shortcuts stay in the field (`Services/EditableTextShortcutPolicy.cs`) | 1.8.0 | #237 |
| I-beam forced above the tool cursor in fillable text fields | 1.8.0 | #235 |
| Signature dropped onto a field is no longer hidden behind it (fields sit below the annotation layer) | 1.7.0 | #156 |
| Form-aware OCR: existing field geometry as recognition boundaries, numeric constraints, comb cells, max length, choice-list validation (`Services/FormAwareOcr.cs`, `FormOcrPolicy.cs`) | 1.8.0 | #242 |

### 3.8 OCR

| Feature | Version | Refs |
|---|---|---|
| Japanese, Czech, Hungarian, Italian, Kazakh, Polish, Russian OCR models (13 total: eng, ben, ces, deu, spa, fra, hun, ita, jpn, kaz, pol, rus, tur; `Services/OcrCatalog.cs`, `OcrLanguages.cs`) | 1.6.4 to 1.8.1 | #138, #293 |
| OCR every selected page and copy combined text | 1.8.1 | #297 |
| "Use High Quality Models" toggle keeps the menu open and refreshes "(download)" labels | 1.6.5 | |
| Download progress/cancellation hints translated; cancellation propagates through the response stream | 1.7.5, 1.8.0 | #227 |
| Form-aware OCR (see 3.7) | 1.8.0 | #242 |

### 3.9 Signing

| Feature | Version | Refs |
|---|---|---|
| Certificates on Windows-compatible USB tokens (`Services/Signing/WindowsCertificateStore.cs`, `ICertificateProvider`, `PfxFileCertificateProvider`); editable visible layout with template fields, live preview, page placement, dimensions, text size | 1.8.0 | #125 |
| Signing goes through the engine's CAdES writer (`KillerCmsSigner.cs`, `PdfSigner.cs`); PDFsharp 6.2 dependency removed | 1.8.0 | |
| Saves strip dead signature values and `/Perms` after an edit (fields kept for re-signing) | 1.6.4 | |
| Pre-save signature scrub no longer NREs on documents without `/AcroForm` | 1.6.5 | |

### 3.10 Printing

| Feature / fix | Version | Refs |
|---|---|---|
| Odd/even page selector (manual duplex) | 1.6.5 | #134 |
| Paper size and paper source selectors; collapsible PRINTER / LAYOUT / OUTPUT sections | 1.7.2 | #186 |
| Print composes and spools on a dedicated thread with a responsive progress window; layout choices frozen at job start (PR #228, PR #119) | 1.7.4, 1.6.2 | |
| Copies / custom scale numeric spinners; last printer, orientation, color, two-sided remembered; Enter/Esc in dialogs | 1.6.1 | #109, #111 |
| Extra copy on some printers fixed (PR #107) | 1.6.1 | #83 |
| Odd/even filter actually reaches the job (PR #159) | 1.7.0 | #155, #157 |
| Huge page range no longer freezes; unmatched range disables Print instead of printing everything (PRs #220, #222) | 1.7.4 | |
| Grayscale output enforced when chosen (`Services/PrintColorConverter.cs`) | 1.8.0 | |
| Turning off two-sided explicitly overrides driver duplex defaults | 1.8.1 | #284 |
| Print preview scrollbar/chevrons follow the theme | 1.7.4 | PR #219 |

### 3.11 Export, CLI, batch

| Feature | Version | Refs |
|---|---|---|
| Export pages as images (PNG/JPEG, 24-1200 DPI, page range, annotations burned, rotations honored) | 1.6.5 | #132 |
| Exported images carry the chosen DPI in metadata | 1.7.2 | #188 |
| JPEG export composites over white (was black); PNG no longer transparent unless `--transparent` | 1.6.6 | #148 |
| Full CLI: `--merge`, `--extract-pages`, `--split`, `--decrypt`, `--to-image`, `--flatten`, `--print`, `--ocr`, `--batch-resave`, `--version`, `--help`, `--verify`; meaningful exit codes; works while the GUI is open (`Features/Cli/CliRunner.cs`, `BatchRunner.cs`) | 1.6.4, 1.8.0 | |
| Remove Password in the Save dropdown | 1.6.6 | #149 |
| Save Flattened file-name compatibility (`Services/SaveFileNamePolicy.cs`) | 1.8.0 | #260 |
| Merge validates each input with the engine and routes unreadable PDFs through repair | 1.8.0 | |

### 3.12 File dialogs and window chrome

- **Every Open/Save dialog replaced by KillerPDF's own themed picker** (1.7.0,
  `Controls/FileDialog.xaml(.cs)`, 1,386 lines; `Controls/FolderTree.cs`, `Controls/Picker.cs`,
  `PickerStyles.xaml`): places rail, folder tree, list/icon/details views, sortable columns,
  pinnable folders, recents, multi-select. Shared across the "Killer Tools" family. Remembers the
  last folder per operation kind (#178); image pickers remember their own folder (1.7.4); Explorer
  Quick Access pins are merged in, with a crash fix when Quick Access is unreadable (#210); wheel
  scrolls the multi-column list horizontally (1.7.1); live image preview pane (1.7.3); selected
  rows no longer get a dark text-stroke (1.8.0).
- Themed Windows system menu on caption right-click / Alt+Space (`Shell/SystemMenu.cs`, 1.7.0).
- Rounded, lifted document card with drop shadow; family divider that lights in accent on hover;
  content border lightened; active tab ring/underline follow the accent (1.7.0, 1.7.3).
- Password Required prompt themed (1.6.6); quit prompt themed with "reopen next launch" +
  remember (1.6.1, #105); no quit prompt when nothing is open (1.6.3).
- App-wide chrome scale 70% to 250% via wheel over the logo or Ctrl+Shift+plus/minus/0
  (`Shell/AppScale.cs`, 1.6.5); transient status readout (1.7.0).
- Title-bar logo click/drag crash (`EntryPointNotFoundException` for `SendMessage` in user32,
  a bad P/Invoke signature) fixed (1.8.1, #298); drag-to-restore over the logo on every theme
  (1.7.4, #206).
- Settings panel dissolved: theme and language are rail flyouts (`Shell/RailFlyouts.cs`),
  toolbar appearance is a toolbar right-click menu with independent icon size and text placement
  (Ctrl+Shift+1..6), privacy toggles moved to About (1.7.0).
- "Confirm before opening links" toggle on About (off by default); link target shown in the
  status bar on hover (1.6.2, 1.7.0).
- Recent-files privacy: Clear list on the start screen; "Don't remember recently opened files"
  (1.6.5, #146).
- New app icon set; the old red-bar document icon now marks PDF files (1.7.0).

### 3.13 Themes

- 1.7.2 added **98SE** (classic Windows 98 chrome: caption bars, bevels, no grain, no fades,
  flat badge), **Ectoplasm, Decay, Mourning, Sepulchre, Delirium, Malaise** on top of Dark, Light,
  Black, Blood, Greed, Cyanotic. Accent overlays exist for Dark, Light, Black and 98SE
  (`Themes/Accents/<Family>/{Blue,Green,Orange,Purple,Red,Teal}.xaml`), persisted per family
  (`DarkAccent`, `LightAccent`, `BlackAccent`, `98SEAccent`). `HighContrast` migrated to `Black`.
- 1.7.2 made themes fully repo-owned again (a private sibling `KillerUI` folder used to overlay
  resources at runtime).
- Fixes: theme flyout no longer jumps for themes without swatches (#199); switching themes with a
  bar open rebuilds it in both panes (1.7.5); 98SE gray chip / zero-opacity fades no longer leak
  into other themes (1.7.4); Black theme selection color (1.6.5); About update button hover in
  98SE (1.8.0). Issue #303 (option to disable grain) was closed 08-29, after 1.8.1.

### 3.14 Keyboard and accessibility

- F1 shortcuts overlay with LIST / KEYBOARD views; the visual keyboard (`Shell/KeyboardMapOverlay.cs`)
  and the list are generated from one table (`Services/ShortcutTable.cs`, 69 entries across
  Base/Ctrl/CtrlShift/Shift/Alt layers) so they cannot drift; key names translatable (1.6.4, 1.7.5).
- Layout-aware shortcut matching (`Services/KeyLayout.cs`): Ctrl+? and Ctrl+= respond to the key
  that types the character on German/French/Nordic layouts; overlay prints the right spelling (#153).
- Remaps: About F2 to F12, Document Info F12 to F4 / Ctrl+D, F2 renames bookmark, F3/Shift+F3
  next/prev match, Settings F9 then sidebar F9, Ctrl+, retired, Ctrl+Shift+W close others,
  Shift+F4 file size, Alt+M hide toolbar, N night mode, Shift+N invert images, F10 split.
- Tooltips show shortcuts everywhere; menus show shortcuts dimmed at the right edge with icons in
  the gutter (1.6.4, 1.6.5, 1.6.6, 1.7.2).
- Issue #190 (customizable shortcuts) remains open.

### 3.15 Localization

- New locales: Japanese (1.6.2, #118), Czech (1.6.5, #138), Polish (1.7.2, #191), Hungarian
  (1.7.4, PR #214), Italian (1.8.0, PRs #234/#257/#262/#268/#280), Kazakh (1.8.0, PR #274),
  Russian (1.8.1, PR #294). 15 total; `Strings/TRANSLATING.md` moved to root `TRANSLATING.md`.
- `--lang-file` loads an external translation with live reload on save for translators (1.7.4, #211).
- `KillerPDF.Tests/LocalizationParityTests.cs` gates key parity; release.ps1 gates translation
  parity (1.7.5).
- Repeated hardcoded-string sweeps: status/dialog strings (1.6.2), "Page N" labels (#137), mojibake
  in ja/es/bn/zh Document Info labels (#136, 1.6.6), default-viewer prompt (1.7.0), dialog titles,
  file filters, errors, busy overlays, DRAFT watermark, OK/Cancel/Yes/No, recents "missing",
  copy/paste/delete confirmations, search states, OCR download hints (#227, 1.7.4/1.7.5), key and
  mouse names (#230), forced newlines in nine dialogs via `xml:space="preserve"` (#231), remaining
  signature/text-edit/install/publisher messages (1.8.0), repair prompt (#299), fillable field tool
  (#295), all 15 languages completed (#286). German refinements (#114, #126, #150).
- RTL: multi-line highlights follow reading direction per line for Persian/Arabic/Hebrew (#170, 1.7.1).

---

## 4. Bugs fixed (by area, with issue numbers)

### 4.1 Crashes

| Symptom | Fix | Version | Refs |
|---|---|---|---|
| Startup crash on older Windows 10 / .NET Framework builds | | 1.6.1 | #101 |
| Crash saving a freshly merged or imported PDF | | 1.6.1 | #112 |
| "Cannot retrieve stream length" on save | recovered automatically | 1.6.1 | #106 |
| Zero-page page tree crashed Continuous view | index guarded | 1.6.4 | #130 |
| Intermittent native heap corruption while scrolling | every direct PDFium call now holds the render lock (PDFium is single-threaded) | 1.6.4 | |
| NRE opening Stamp/Transform or saving a multi-line text annotation | burn used justified alignment whose path dereferenced empty line-break blocks; now left-aligned, vendored formatter patched; fallback font; failing annotation skipped (PR #144) | 1.6.5 | #142 |
| NRE in pre-save signature scrub on documents without forms | absent dictionary entries treated as absent | 1.6.5 | |
| NRE in pre-save link-border strip on `/Subtype`-less link | skipped | 1.7.0 | |
| `'∞' is not a valid value for property 'Height'` on page click | form rectangles and sized annotations validated before reaching WPF; legacy zero-size signatures fall back | 1.7.1 | #181 |
| Array-index error on owner-restricted encrypted PDFs with malformed linearization (Fritzbox manual) | routed through PDFium cleanup instead of PdfSharp read-only parser | 1.7.1 | |
| Native teardown crash after enabling Docnet annotation rendering | render through direct PDFium layer owning the form-fill callback memory; close form/page/doc together | 1.7.1 | #141, #179 |
| Cross-thread crash in render cache budget | | 1.7.2 | |
| `RuntimeBinderException` on Quick Access under Wine/CrossOver (also protects Windows machines with a broken shell namespace) | | 1.7.4 | #210 |
| NRE opening a PDF from Explorer while still starting | forwarded path replayed from `Loaded` | 1.7.4 | #202 |
| `AggregateException` unobserved task fault reported as a crash | observed faults no longer reported as crashes; recoverable crash dialogs deferred | 1.8.0 | #263 |
| `killerpdf:` handoff crash when it is the launch that starts the app | | 1.8.0 | #276 |
| `EntryPointNotFoundException: SendMessage in user32.dll` on title-bar logo click/drag | | 1.8.1 | #298 |
| `WidthAndHeightCannotBeNegative` (#123), NRE (#131) | closed 07-17 and 08-03 without an explicit changelog line | 1.6.4 / 1.7.0 | #123, #131 |

### 4.2 Save integrity and PDF structure

| Problem | Fix | Version | Refs |
|---|---|---|---|
| Any save of a PDF without bookmarks left a dangling `/Outlines` reference (strict viewers demanded repair, repair stripped forms) | saves clean; repair first tries a lossless PDFium re-save preserving forms and bookmarks | 1.6.3 | #103 |
| Zero-size `/CropBox` planted on every page (Adobe "page dimensions out-of-range") | page boxes read without touching the document; degenerate crop boxes stripped on save | 1.6.3 | |
| Imported images with broken DPI produced pages outside Adobe's 3 to 14,400 pt range | clamped on import; offer to rescale existing out-of-range pages on save | 1.6.2 | |
| Resaving reduced PDF/A conformance | PdfSharpCore vendored with six patches (no Producer/Creator stamping, no `/ModDate` rewrite, no `/Group` injection, exact `/Length`, lowercase booleans, no verbose layout) | 1.6.4 | |
| Digital signature value kept after an edit | dead signature values and `/Perms` stripped | 1.6.4 | |
| "File in use" saving over a PDF with annotations but no readable links | cached PDFium link handle released before save | 1.6.4 | #129 |
| Bookmarks pointing at named destinations (wkhtmltopdf) dead | resolved through the name-tree walker (PR #143) | 1.6.5 | #133 |
| Bookmark titles in encrypted PDFs shown as mojibake | re-decoded | 1.6.4 | #133 |
| Page numbers/watermarks not written when they were the only markup | | 1.6.5 | #147 |
| Stamps could not be removed | Apply allowed with both sections off | 1.6.5 | #145 |
| Rotating a page opened with non-zero `/Rotate` swapped its MediaBox on save | vendored PdfSharpCore landscape-flip fix | 1.7.2 | #184 |
| Malformed CropBox outside MediaBox preserved | stripped on save | 1.7.1 | |
| Link annotations missing the print flag (PDF/A-4) | | 1.8.0 | |
| Typst `/XYZ` destinations with zero zoom disabled outlines | zero accepted as "keep zoom" | 1.8.0 | #269 |
| Tagged PDFs could not be appended after untagged pages | | 1.8.1 | #300 |
| Repair dialog / damaged drops | repair offered from Pages panel; localized | 1.7.4, 1.8.1 | #203, #299 |

### 4.3 Rotation and annotation placement (#169 saga, terada-d)

1. 1.7.0: burn-in was never told the page's rotation, so anything placed on a quarter-turned
   page came out rotated 90 degrees, offset and axis-swapped; stamps landed in the wrong corner.
   Rotation now travels with the burn (editor and print flatten).
2. 1.7.1: the fix read only the temporary rotation map, which is empty until a page op triggers a
   temp save; burn now falls back to the page's own `/Rotate`; opening a document clears the
   previous map.
3. 1.7.4: rotating a page deleted unsaved annotations (`SaveTempAndReload` default cleared
   overlays); they now turn with the page (`Services/AnnotationRotate.cs`).
4. 1.7.5: items near a shrinking edge are clamped inside the post-rotation frame instead of
   vanishing off-page; A3 both orientations and both directions covered by tests
   (`KillerPDF.Tests/AnnotationRotateTests.cs`, `PdfBurnRotationTests.cs`).
5. 1.8.0: typewriter text keeps its vertical position after save (WPF baseline vs. one-em
   assumption, #273).

### 4.4 Rendering, memory, performance

| Problem | Fix | Version | Refs |
|---|---|---|---|
| Blurry pages at high zoom / high DPI in Continuous | visible pages re-render at higher resolution | 1.6.1 | #85 |
| 243-page image-heavy PDF climbing past 7 GB | bitmap cache windowed around the viewport; heap compaction on tab close | 1.6.3 | #122, #100 |
| Startup time and memory | re-sharpen pass at device resolution not 2x (PR #194); cache budgeted in bytes (~160 MB per tab); re-render on DPI change | 1.7.2 | #189 |
| Right page blurred in Two-Page view | | 1.8.0 | #248 |
| Grid view: last column dropped into next row; blank rebuild on pane resize; unfocused pane margin; render failure stranding tiles; tiles select on any interaction; zoom seams settle in one pass | | 1.7.4, 1.7.2, 1.7.5 | |
| Grid never tracked current page while scrolling | follows tile nearest viewport center | 1.6.4 | |
| Grid to Continuous kept grid scrollbar overrides | | 1.6.3 | |
| Selection boxes stranded until restart | removed from their real layer; swept on close | 1.6.3 | #121 |
| Ctrl+0 / Ctrl+1 not a true 100% outside Continuous (PR #154) | | 1.7.0 | #152 |
| Fast wheel scrolling carried momentum into an accidental page flip in Single/Two-Page (`Services/WheelPageFlipGate.cs`) | one deliberate geared notch at an edge accepted; momentum suppressed | 1.7.5, 1.8.0 | #205 |
| Ctrl+Tab sent the document back to the beginning | | 1.8.0 | #261 |
| Two-Page arrows/PgUp/PgDn move one spread | | 1.6.3 | #120 |
| Continuous view click snap-scrolled the page | clicks are for tools only; current page follows the viewport | 1.6.4 | #128 |
| View-mode switch flashed | cross-fade | 1.6.2 | |
| Stale rendering cancelled on document change; viewer layout updates hardened | | 1.8.0 | |
| Existing PDF annotations/stamps/ink/filled fields invisible in viewer, print, flatten, export, thumbnails (PDFium does not paint appearance streams unless asked) | painted through direct PDFium layer with owned form-fill environment | 1.7.1 | #141, #179 |

### 4.5 Text editing and fonts

| Problem | Fix | Version | Refs |
|---|---|---|---|
| Editing a line collapsed it to 3 pt (`/F1 1 Tf` with matrix scaling) | use PointSize, fall back to FontSize then line height (PR #165) | 1.7.0 | #163 |
| Word-level font name fallback joined letter names into an unresolvable string | read the letter's name (PR #166 discussion) | 1.7.0 | #166 |
| Bold/italic lost on double-click edit | family/face split (`Services/PdfFontStyle.cs`) | 1.7.1 | #182 |
| PostScript font names not mapped to Windows families | | 1.7.2 | #187 |
| CJK/Bengali/Korean/Thai/Arabic/Indic text saved as `.notdef` boxes | `.ttc` reading + coverage fallback chain | 1.7.0 | #168 |
| Live text editing stability | | 1.8.0 | |

### 4.6 Install, update, registration

| Problem | Fix | Version | Refs |
|---|---|---|---|
| Self-updater hash drifted from the binary | reads `SHA256SUMS.txt` from release assets | 1.6.2 | |
| All-users install registered PDF handler in the admin's HKCU | HKLM registration for every account | 1.7.1 | #176 |
| `killerpdf://` handler not machine-wide | | 1.7.2 | #183 |
| Per-user beside all-users installs; failed elevated uninstall reported success | scope guard, dual-install repair, elevation request | 1.7.4 | |
| Duplicate installer relaunch during all-users handoff | | 1.8.0 | #238 |
| Silent install proceeded without .NET 10 runtime leaving a half-install | exit code 10 before writing | 1.8.0 | #275 |
| `--verify` flagged the launcher's own marker | whitelisted | 1.8.0 | #279 |
| Stale per-user protocol key shadowed HKLM handler | cleared on move to all-users (PR #258) | 1.8.0 | #246 |
| Portable run hijacked the protocol handler | | 1.8.0 | #267 |
| Portable launcher: late delete failure reported as startup error, `/register-user` reached the extracted child, dead env var | | 1.8.0 | #259 |
| Legacy install upgrade not smooth | closes legacy installs safely; failures in themed notices; duplicate scope repair keeps settings/PDFs | 1.8.1 | #285 |
| Portable launcher publish with .NET 10 SDK tried to copy an unused binding-redirect config | | 1.7.5 | |

### 4.7 UI / dialogs / misc

| Problem | Version | Refs |
|---|---|---|
| Toolbar dropdown carets missing on Windows 10 (use ChevronDown E70D, PR #108) | 1.6.1 | #104 |
| Recent-files X button clipped | 1.6.1 | |
| Two stacked prompts on close with unsaved changes; default No | 1.6.3 | |
| Scrollbar corner ownership when both bars visible | 1.6.3 | |
| App-size readout parked on the status bar | 1.7.0 | |
| Shortcut window columns too narrow; list uses full width; wider and more spacing | 1.7.1, 1.8.0 | #177, #230 |
| Picker radio dot off-center (PR #198), accent swatch centering (PR #208) | 1.7.2 | |
| Checkbox labels clipped in long languages; dropdown max height (PRs #224, #225) | 1.7.4 | #223 |
| Empty-state recents panel ignored window resizing | 1.7.4 | |
| Split pane proportions lost on snap/maximize/restore; closed sidebar reopened on tab load | 1.7.4 | |
| Sidebar blank after install relaunch / session restore until a pane was clicked | 1.7.4 | |
| Sidebar page total drifted from the pages it counts (PR #265) | 1.8.0 | |
| Fullscreen bounds and tab thumbnails | 1.8.0-beta.4 | |
| Document opened from Explorer had no keyboard focus | 1.7.2 | #196 |

### 4.8 Security

- SixLabors.ImageSharp 1.0.4 to 2.1.13 (seven published DoS / out-of-bounds advisories; image
  import, clipboard paste and signature images all pass untrusted data through it) (1.6.4).
- Installed-payload verification rejects files absent from the signed manifest so no untrusted
  assembly can sit in the probing path (1.8.0).
- Engine: bounded parsing with explicit implementation limits; fail-closed graph imports;
  oversized xref/object streams rejected.

---

## 5. Validation and release process (new since 1.6.0)

- `validation/Compare-VeraPDF.ps1` + `KillerPDF.exe --batch-resave`: every release resaves a
  2,907-file public conformance corpus (veraPDF test corpus, Isartor, TWG) and diffs veraPDF
  reports file by file; requirement is zero regressions. 1.8.1: 2,898 resaved, 9 deliberate
  skips, 0 failures, 0 regressions, 74 files came out more conformant.
- `validation/QpdfSweep.ps1`: `qpdf --check` before/after on every saved file.
- `validation/Benchmark-Versions.ps1`: alternating five-run median batch-resave benchmark
  between two builds (`validation/PERFORMANCE.md`).
- `release.ps1` gates: strict Release build with zero warnings, unit tests (app + engine),
  translation parity, `ReleaseDate` matches the changelog, packaging + launcher smoke test,
  Authenticode.
- Engine: 2,907-file incremental structural corpus gate, selected-page import gate, veraPDF
  PDF/A-4 and PDF/UA-2 smoke validation of generated fixtures, OpenSSL verification of real
  detached CMS signature fixtures.

---

## 6. Issue index (#83 to #304)

Resolution version is taken from the changelog reference; "closed" alone means closed on GitHub
without a changelog line. Non-Windows items are marked and otherwise ignored.

| # | Title (abridged) | Resolved | Notes |
|---|---|---|---|
| 83 | Print problems (extra copy) | 1.6.1 | PR #107 |
| 84/85 | Blurry at high zoom on high-DPI | 1.6.1 | |
| 88 | Text box fill/background | 1.6.0 | already in fork base |
| 89/96 | XRef crash `Invalid entry in XRef table` | closed 06-23/27 | pre-1.6.0 or superseded by engine |
| 90 | Windows thumbnails | closed 06-27 | |
| 91 | Change download destination | closed | question |
| 93/95/102 | German / French | 1.6.0 base + #114 | |
| 94 | Slow search on large document | closed 06-28 | |
| 97/98/99 | Edit issues, no re-render after delete all, remove watermark | closed 07-02 | superseded by 1.6.5 stamp fixes |
| 100 | Memory too big | 1.6.3 | see #122 |
| 101 | NRE startup crash (Win10 1809) | 1.6.1 | |
| 103 | Form reopen error (dangling /Outlines) | 1.6.3 | |
| 104/108 | Icons/carets not rendering on Win10 | 1.6.1 | |
| 105 | Forget previously opened files | 1.6.1 / 1.6.5 | reopen prompt; privacy toggle #146 |
| 106 | Cannot retrieve stream length | 1.6.1 | |
| 107 | PR: print copies fix | 1.6.1 | |
| 109 | Print dialog + toolbar UX | 1.6.1 | |
| 110 | Print PIN issue | closed 07-02 | |
| 111 | PR: Enter/Esc in dialogs | 1.6.1 | |
| 112 | ArgumentException path crash on save | 1.6.1 | |
| 113 | Why not edit original text boxes | closed | question |
| 114/124/126/150 | German updates | 1.6.1 to 1.7.1 | |
| 115 | PR: hyperlinks in object-stream/linearized PDFs (read via PDFium) | 1.6.2 | |
| 117 | Read-only by default | 1.6.2 | PgUp/PgDn navigate instead of reorder |
| 118/136/164/302 | Japanese | 1.6.2 to 1.8.1 | |
| 119 | PR: print progress scrim | 1.6.2 | |
| 120 | Two-page reading enhancements | 1.6.3 | |
| 121 | Selection box persists | 1.6.3 | |
| 122 | 7 GB RAM | 1.6.3 | |
| 123 | WidthAndHeightCannotBeNegative crash | closed 07-17 | |
| 125 | USB token signing | 1.8.0 | |
| 127 | Flowing text selection | 1.6.5 | |
| 128 | Continuous click snap | 1.6.4 | |
| 129 | Save over existing fails | 1.6.4 | |
| 130 | Page index out of range | 1.6.4 | |
| 131 | NRE (Russian locale report) | closed 08-03 | |
| 132 | Export pages as images | 1.6.5 | |
| 133 | Bookmarks (mojibake, named destinations, editing) | 1.6.4 / 1.6.5 | |
| 134 | Odd/even printing | 1.6.5 | |
| 135 | Invert / dark mode | 1.6.5 / 1.7.0 | |
| 137/138 | "Page N" localization, Czech | 1.6.5 | |
| 139/140 | UTF-16 vendored sources corrupted by EOL normalization | 1.6.5 | |
| 141 | Existing annotations not shown | 1.7.1 | |
| 142/144 | Multi-line text burn crash | 1.6.5 | |
| 143 | PR: named destinations | 1.6.5 | |
| 145 | Page numbering cannot be turned off | 1.6.5 | |
| 146 | Recent files privacy | 1.6.5 | |
| 147 | Stamps not saved | 1.6.5 | |
| 148 | JPG export black | 1.6.6 | |
| 149 | Remove password | 1.6.6 | |
| 151 | Page tooltip depends on view mode | 1.7.0 | |
| 152/154 | Reset zoom 100% | 1.7.0 | |
| 153 | Shift-layout shortcuts | 1.7.0 | |
| 155/157/159 | Odd/even ignored in real job | 1.7.0 | |
| 156 | Signature hidden behind field | 1.7.0 | |
| 158 | Comb fields | 1.7.2 / 1.8.0 | |
| 160 | PDF comparison | 1.8.0 | |
| 161 | macOS Intel | closed | non-Windows, ignored |
| 162 | Measurement tool | 1.8.0 | |
| 163/165 | 3 pt font on edit | 1.7.0 | |
| 166 | FontName fallback | 1.7.0 | |
| 167 | Fine rotation resizes landscape page | 1.7.0 | |
| 168 | CJK saved as boxes | 1.7.0 | |
| 169 | Annotations burned in unrotated frame | 1.7.0 to 1.7.5 | see 4.3 |
| 170 | RTL multi-line highlight | 1.7.1 | |
| 171 | PR: inline Print Shop panel | closed, not merged | |
| 172 | Drag PDFs into Pages panel | 1.7.2 | |
| 173 | Grayscale / binarization | 1.8.0 | |
| 174 | Contrast levels | 1.7.2 | |
| 175 | Trapezoid correction | 1.7.1 | |
| 176 | Machine-wide registration | 1.7.1 | |
| 177 | Shortcut columns narrow | 1.7.1 | |
| 178 | Open dialog folder navigation | 1.7.1 | |
| 179 | Prints blank, wrap, browser behavior (D&D Beyond form) | 1.7.1 | |
| 180 | PR: form appearance streams | 1.7.1 | |
| 181 | Infinity Height crash | 1.7.1 | |
| 182/187 | Text formatting lost | 1.7.1 / 1.7.2 | |
| 183 | killerpdf:// machine-wide | 1.7.2 | |
| 184 | MediaBox swap on rotated save | 1.7.2 | |
| 185 | Two-column selection | 1.7.2 | |
| 186 | Paper size in print | 1.7.2 | |
| 188 | Export DPI metadata | 1.7.2 | |
| 189 | Startup time and memory | 1.7.2 / 1.7.4 | |
| 190 | Customised shortcuts | open | |
| 191 | Polish | 1.7.2 | |
| 192 | README as docs | closed 08-09 | website help page |
| 193 | Cover/book view | 1.7.2 | |
| 194 | PR: re-sharpen at device resolution | 1.7.2 | |
| 195 | Assorted suggestions | closed 08-15 | |
| 196 | Touchpad horizontal scroll, focus on open | 1.7.2 | |
| 197 | Page badge | 1.7.2 / 1.7.4 | |
| 198/208 | PR: picker centering | 1.7.2 | |
| 199 | Theme flyout jumps | 1.7.2 | |
| 200 | Highlight blend | 1.7.2 | |
| 201 | Remember zoom | 1.7.2 | |
| 202 | NRE opening during startup | 1.7.4 | |
| 203 | Damaged PDF not added to Pages | 1.7.4 | |
| 204 | Transform multiple pages | 1.8.0 | |
| 205 | Wheel page flip too fast | 1.7.5 / 1.8.0 | |
| 206 | Drag-restore over logo | 1.7.4 | |
| 207 | Export page as image in context menu | 1.7.4 | |
| 209 | Shift+wheel | 1.7.5 | |
| 210 | Quick Access crash | 1.7.4 | Wine report, fix is generic |
| 211 | Translation testing | 1.7.4 | |
| 212 | Resizable thumbnail grid | closed 08-19 | no changelog line |
| 213 | Drag pages between documents | 1.8.0 | |
| 214 | PR: Hungarian | 1.7.4 | |
| 215 | Fullscreen on top | 1.7.4 | |
| 216/217 | Shortcut labels, file-size string | 1.7.4 | |
| 218 | PR: PolySharp EmbeddedAttribute | 1.7.4 | |
| 219/220/222/224/225 | PRs: print/dialog fixes | 1.7.4 | |
| 221 | I-beam over text | 1.8.0 | |
| 223 | Dropdowns too small | 1.7.4 | via #224/#225 |
| 226 | Horizontal scroll gesture when zoomed | closed 08-21 | related to #196/#209 |
| 227 | Translation missing | 1.7.4 / 1.7.5 / 1.8.0 | |
| 228 | PR: print off UI thread | 1.7.4 | |
| 230/231 | Untranslated strings, forced newlines | 1.7.5 | |
| 232 | Letter spacing | 1.8.0 | |
| 233 | Batch page ops | 1.8.0 | |
| 234/257/262/268/280 | Italian | 1.8.0 | |
| 235 | Form text overlaid, cursor hidden | 1.8.0 | |
| 236 | pdfium integrity check dead since 1.7.4 | 1.8.0 | `--verify` |
| 237 | Ctrl+S dead in form field | 1.8.0 | |
| 238 | All-users install problem since 1.7.4 | 1.8.0 | |
| 242 | Form-aware OCR | 1.8.0 | |
| 244 | Form save fails above U+00FF | 1.8.0 | |
| 245 | Push buttons, list boxes, multi-select | 1.8.0 | PRs #249-#254 |
| 246 | Stale per-user protocol key | 1.8.0 | PR #258 |
| 248 | Right page blurred in Two-Page | 1.8.0 | |
| 255 | 1.7.x rejects PDF > 1.9 header | 1.8.0 | |
| 256 | Form save re-embeds fonts | 1.8.0 | |
| 259 | Portable launcher trio | 1.8.0 | |
| 260 | Flatten file-name compatibility | 1.8.0 | |
| 261 | Ctrl+Tab resets to beginning | 1.8.0 | |
| 263 | AggregateException crash | 1.8.0 | |
| 264 | Preflight (image resolution, separations, trim box) | open | |
| 265 | PR: sidebar page total | 1.8.0 | |
| 266 | Predictable Ctrl+Z | 1.8.0 | |
| 267 | Portable steals protocol handler | 1.8.0 | |
| 269 | Outline does not work (Typst) | 1.8.0 | |
| 270 | ARM64 executable | open | Windows on ARM, not pursued |
| 271 | Touch input | 1.8.0 | |
| 272 | Renderer replacement suggestion | closed 08-27 | declined; PDFium stays |
| 273 | Text position way off | 1.8.0 | |
| 274 | PR: Kazakh | 1.8.0 | |
| 275 | Silent install without runtime | 1.8.0 | |
| 276/277/278 | killerpdf: launch crash / refusal reason | 1.8.0 | |
| 279 | --verify marker false positive | 1.8.0 | |
| 281 | Adobe error 109 | open | |
| 283 | Insertion stripe when dragging pages | 1.8.0 | |
| 284 | Duplex not disabled | 1.8.1 | |
| 285 | Installer not smooth | 1.8.1 | |
| 286 | Translations missing | 1.8.1 | |
| 287 | Page quality does not reduce size | 1.8.1 | |
| 288 | Comments (annotation replies) | open | |
| 289 | Ctrl+A in Pages | 1.8.1 | |
| 290/291 | Transform progress, window size | 1.8.1 | |
| 292 | Measurement readout tiny | 1.8.1 | |
| 293/294 | Russian | 1.8.1 | |
| 295 | Fillable text field tool untranslated | 1.8.1 | |
| 296 | Delete key removes pages | 1.8.1 | |
| 297 | OCR multiple pages | 1.8.1 | |
| 298 | SendMessage entry point crash | 1.8.1 | |
| 299 | Repair dialog untranslated | 1.8.1 | |
| 300 | Damaged PDF not appended | 1.8.1 | |
| 301 | Continuous view request | open | changelog cites #301 for the wheel-lines fix |
| 303 | Option to disable grain | closed 08-29 | post-1.8.1 |
| 304 | PR: French update | open | |

---

## 7. What is and is not transferable to Scalpel

Scalpel stays on `net48`, PdfSharpCore, a single Costura EXE, an MSIX Store channel, and its own
"Clinical" ribbon UI, so upstream changes fall into three buckets.

**Not transferable as-is (platform-bound):**

- The KillerPDF.Engine (`net10.0` only; ADR-002 rejected multi-targeting to net48). Its
  *behaviors* are the useful part: the save sanitizer (empty `/Outlines` root, degenerate
  CropBox), signature invalidation on edit, byte-preserving incremental saves, Multiply
  highlights, isolated Form XObject overlays for burn-in, page-dimension normalizer.
- `KillerLauncher` payload packaging, `--verify`, `/silent`, the two-package split, .NET 10
  runtime detection. Scalpel's Store/MSIX and Inno channels already own install.
- `killerpdf://` protocol handler (Scalpel would need its own scheme; MSIX declares protocols in
  the manifest).
- `LibraryImport`, UTF-8 P/Invoke marshalling, `CodePagesEncodingProvider` bootstrap.

**Directly portable ideas (net48-compatible, mostly WPF/code-behind, already in Scalpel's
partial-class shape):**

- Bug classes Scalpel likely shares from the 1.6.0 base: dangling `/Outlines` (Scalpel has
  `PdfSaveGuard` and `PdfReopen` for this already), zero-size `/CropBox` on save, out-of-range
  page sizes from bad image DPI, rotated-page burn-in frame (#169, all four stages), PDFium
  lock around every direct call (heap corruption), cached PDFium link handle holding the file
  open (#129), JPEG export black / PNG alpha (#148), multi-line justified burn NRE (#142),
  signature scrub NRE without `/AcroForm`, `/Subtype`-less link NRE, infinity Height crash
  (#181), non-A4 form overlay offset (1.6.6), form appearance streams with `/Length`, multiline
  and invariant-culture numbers (#180, 1.7.4), 3 pt font on edit (#163), bold/italic lost on
  edit (#182), PostScript font-name mapping (#187), `.ttc` collection reading + coverage
  fallback (#168; Scalpel has Noto bundling for Hebrew/Arabic/Cyrillic but not the general
  chain), CJK bookmark mojibake, named-destination bookmarks (#143), print odd/even filter,
  huge print range freeze, print on a worker thread (PR #228), layout-aware shortcut matching
  (#153, relevant for the Hebrew layout), two stacked close prompts, bitmap cache budget in
  bytes + windowed continuous view (#122/#189), re-sharpen at device resolution (PR #194),
  stale Explorer-open NRE during startup (#202), Ctrl+Tab reset (#261).
- Features: Shapes tool, flowing text selection/highlight, column-aware selection, letter
  spacing, comb fields, Remove Password, export pages as images with DPI metadata, odd/even
  printing, paper size/source, night mode with image carve-out, page badge, jump history,
  Home/End and Ctrl+1/2/3, redo, bookmark editing, Transform LEVELS / perspective / batch /
  page quality, Pages-panel drops, Ctrl+A and Delete in the Pages panel, insertion-line drop
  marker, measurement tool, PDF comparison, form-aware OCR, OCR of selected pages, USB-token
  signing (Scalpel's roadmap "signing tiers" spec overlaps), touch pan/pinch, shortcut table as
  a single source of truth for list + visual keyboard, localization parity tests, external
  translation file with live reload.

**Design-language-bound (port the behavior, not the look):** themed file dialogs, system
menu, rail flyouts, 13 themes, app-scale, rounded card chrome. Scalpel's ribbon and
Light+Red Clinical theme own that space.

See `docs/PDF-FEATURE-OPPORTUNITIES.md` and the `killerpdf-feature-port-program` memory for the
tiered port list; this document is the raw inventory it should draw from.

---

## Addendum: upstream v1.8.2 and v1.8.3 (checked 2026-09-01)

Upstream released **v1.8.2** on 2026-08-31 and is mid-flight on **v1.8.3**. This addendum records
what changed and how it was triaged, so the next check starts from here rather than from v1.8.1.

### The earlier forecast held

v1.8.2 shipped **#311** (images flipping vertically on annotation burn) and **#314** (Microsoft
Office hybrid-reference PDFs) - the two items this analysis predicted and verified as
not-applicable to Scalpel. Both already have regression guards
(`BurnImageOrientationTests`, `HybridXrefOpenTests` with a hand-built hybrid-xref fixture), so
nothing was needed.

**#312** (context-menu zoom shortcut shown as `Ctrl+=` when the layout needs `Ctrl++`) had already
been fixed on this side a day earlier, and more thoroughly: upstream relabelled the shortcut to
`Ctrl++`, whereas `Services/KeyLayout.cs` asks the active keyboard layout which character the key
actually types, so it prints `Ctrl+=` on US and `Ctrl++` on German rather than being wrong for one
of them.

### Applied

- **Text edits left the original text in the file.** Upstream's note is "text edits now save as
  opaque covers". Scalpel's cover was already opaque, but a cover is not a removal: measurement
  showed that after editing "SALARY-120000-SECRET" to "REDACTED-VALUE" and saving, a text extractor
  returned *both* strings. Anyone editing a name, an address or a figure had not actually changed
  it. Fixed by `Services/ContentTextRemover.cs`, which clears the show-text operands that spell the
  original run.
  - Matching is by **content, not position**: Scalpel already captured the exact original string,
    and locating a run geometrically would mean tracking the text and transformation matrices
    through the stream, where a subtle error deletes the wrong words.
  - The bytes in a PDF are usually not the text - PdfSharpCore writes
    `<00360024002F...>`, glyph indices in a subset font - so `Services/PdfToUnicodeMap.cs` parses
    the font's `/ToUnicode` CMap (`bfchar`, `bfrange`, `codespacerange`) to map codes back to
    characters before matching.
  - **Safety rule:** only string operands are emptied; every operator and byte of structure stays
    put, so the stream cannot become malformed. No confident match means no edit at all, and the
    opaque cover remains as the visual fallback. That case is recorded in the log.

- **#324 / #305 - grayscale and black-and-white output were far larger than needed.** Verified:
  `PageQualityConverter` returned `Bgra32` for every mode, storing three identical channels for a
  gray page and 32 bits per pixel for one bit of bilevel information. Now `Gray8` and `Indexed1`
  respectively. The compact formats have no alpha channel, so they are used only when the image is
  fully opaque (which a rendered page always is); anything genuinely transparent keeps the wide
  format. JPEG has no bilevel form, so a black-and-white page is widened to 8-bit gray before JPEG
  encoding. This landed on Scalpel's export-as-images feature - our Transform is geometric only,
  whereas upstream's carries the colour modes.

### Not applicable

| Upstream item | Why not |
|---|---|
| File-picker sorting, remembered between sessions | Scalpel uses the OS file dialog |
| Fillable fields in tagged PDFs; field-creation rework | Scalpel fills form fields, it does not create them |
| #301 dual-pane comparison zoom | Scalpel's compare is a page-by-page text report by design |
| #325 annotation-eraser naming, Sepulchre hover colours, crash-dialog chrome | Upstream's own UI wording and theme |
| #313 German translation | Scalpel ships no German locale |
| #307 selecting and moving fillable fields | A feature Scalpel does not have; ours are editable overlays |

### Open question, deliberately not decided

**#327 - portable builds keeping data beside the launcher.** Upstream moved portable settings,
signatures and OCR models into a `KillerPDF-Data` folder next to the executable; Scalpel uses
`%LOCALAPPDATA%\Scalpel`. Data beside the executable is closer to what most people mean by
"portable" (carry it on a stick, keep your settings), but it changes where existing users' data
lives and needs a migration path, so it is a product decision rather than a defect.
