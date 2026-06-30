# Changelog

All notable changes to Scalpel are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.1.0 - 2026-07-01

### Added
- **Region OCR** (`OcrService.RecognizeRegionText`) — a Tools ▸ "OCR Region" command that arms a rubber-band capture on the page; the dragged rectangle is mapped to native page fractions (rotation-aware via `CanvasToPdfRect`), rendered, cropped, rotated upright, and OCR'd off-thread, with the recognized text placed on the clipboard.
- **Sign from the Windows certificate store** (`Services/Signing/WindowsCertificateStore.cs`) — Digitally Sign offers a certificate-source step: a `.pfx`/`.p12` file (as before) or a certificate already installed in Windows (smart cards, enterprise-issued certs). The store path lists signing-capable certs from `CurrentUser\My`, shows a themed picker (subject — issuer — expiry), builds the issuer chain, and signs via the new `PdfSigningService.SignFileWithCertificate` (the crypto core is unchanged). OS-guarded so the flow stays portable.
- **RFC-3161 trusted timestamp (PAdES-T)** — an optional "Add a trusted timestamp" toggle attaches a timestamp token (over the signer's signature) as the `id-aa-signatureTimeStampToken` unsigned CMS attribute, so the signature proves *when* it was made and survives the signer cert's expiry. BouncyCastle TSP over HTTP (`Services/Signing/HttpTimestampClient.cs`, default DigiCert TSA, overridable via the `SignTimestampUrl` setting); the `/Contents` reserve grew 16 KiB → 32 KiB to fit the token.
- **Visible signature appearance** — an optional captioned box (signer name, date, and an optional reason) placed in a chosen corner of the first page, instead of an invisible signature. The incremental update emits a Form XObject (`/AP /N`) and gives the widget a real `/Rect` and the Print flag; the appearance is cosmetic and does not change the CMS.
- **Long-term validation (LTV)** (`PdfSigningService.AddDss`) — an optional second incremental update adding a Document Security Store (`/DSS`) with the certificate chain plus any reachable CRLs (`Services/Signing/RevocationCollector.cs`, best-effort CRL fetch from CRL Distribution Points), so a signature can be validated offline years later. Best paired with a trusted timestamp and a CA-issued certificate.

### Notes
- The signing additions are covered by unit tests against in-memory / in-test certificates and a self-contained in-test timestamp authority (the CMS still verifies after timestamping, a visible appearance, and a DSS update). Full Adobe-Acrobat "LTV enabled" validation and OCSP collection remain a follow-up; the DSS currently embeds document-level material (no `/VRI`).

## 2.0.0 - 2026-06-30

### Added
- **Digitally sign (PKI)** (`Services/PdfSigningService.cs`) — add a real cryptographic signature (PAdES / PKCS#7 **detached**, invisible) with the user's `.pfx`/`.p12` certificate. The signature is embedded via a hand-written **incremental update** (new Sig dict + invisible field + AcroForm/`SigFlags` + xref/trailer with `/Prev`) appended after the saved bytes, so the signed `/ByteRange` is never re-serialized (PdfSharpCore's whole-document Save cannot sign). SHA-256 over the ByteRange is signed with **BouncyCastle** (`CmsSignedDataGenerator`, SHA256withRSA, signed attributes, DER). Adds the `BouncyCastle.Cryptography` 2.5.1 (MIT) dependency. Tests prove the embedded CMS verifies against the signer cert and the digest matches. *Caveat:* Adobe trust requires a CA-chained cert; no RFC-3161 timestamp / PAdES-LTV yet.
- **Document tabs** (`MainWindow.Tabs.cs`) — a tab strip of open file paths; switching reuses the open/close/dirty machinery (prompts to save, then reloads the target), Ctrl+Tab / Ctrl+Shift+Tab cycle. Hidden with a single file open; single-document behavior is unchanged (it is not a multi-session refactor — switching reloads from disk).
- **Recent files** (`Services/RecentFiles.cs`, `MainWindow.Recent.cs`) — a most-recently-used list (registry-backed, capped at 10, de-duplicated) on the empty-state start screen and in the Open button's context menu; missing files are filtered out and self-heal on click.
- **Watermark & image stamp** (`Services/WatermarkService.cs`) — a Tools-menu op that draws a semi-transparent rotated/tiled **text watermark** and/or an **image stamp** (scaled, positioned, opacity baked into alpha).
- **Transform pages** (`Services/TransformService.cs`) — a Tools-menu op to rotate (lossless 90° via page `/Rotate`), fine-deskew, scale, and flip H/V over a page range (rasterized path via the existing `IPageRasterizer` for flip/deskew/scale).
- **Color picker with eyedropper** (`Services/ColorConvert.cs`, `Services/ColorPickerDialog.cs`) — an HSV square + hue strip + RGB/hex inputs + a desktop-wide eyedropper, reached from a "+" button on the Draw, Line, and Text color palettes.
- **OCR — live progress + cancel.** Make Searchable now reports per-page progress (`OcrService.MakeSearchable` gains `onProgress`/`CancellationToken`) behind a themed overlay with a Cancel button (and Esc). Plus a language picker, a high-quality (`tessdata_best`) mode, copy-page-text to clipboard, and extract-all-text to `.txt`/`.md`.
- Earlier in this line: a **Line tool** (Shift-snap), **Document Info** editor (Tools / F12), and **full-screen (F11)** with a keyboard-shortcut overlay (F1) and tool letter keys.

### Fixed
- AcroForm field overlays now render in **Continuous / Two-page / Grid** views (they were drawn on the single-page canvas only), and are positioned correctly on PDFs whose **CropBox differs from the MediaBox**.
- The saved-signature chooser is now a themed **dropdown beneath the Sign button** (it used to float in the page and stay hardcoded-dark in Light/High-Contrast themes); clicking away closes it cleanly and it reopens in one click.
- Editing existing right-to-left (Hebrew/Arabic) text keeps correct word order and reads/aligns RTL; editing text matches the original font (falling back to the embedded font or a clearly-named substitute).

### Changed
- The end-to-end UI test harness (`Scalpel.E2E`) was made deterministic — driving radios/toggles through foreground-free UI Automation patterns instead of synthesized clicks — and now passes the full suite headlessly. The four exclusive view-mode toggles became grouped `RadioButton`s (no visual change).

## 1.8.0 - 2026-06-25

### Added
- **Optional update notifications.** Scalpel can let you know when a new version is out. It is **off until you turn it on**, sends nothing about you or your files, and can be toggled anytime in Settings. The notification links straight to the right place to update — the Microsoft Store for Store installs, the website for portable and installed copies (`Services/UpdateService.cs`, reading a single static `version.json`).

### Fixed
- The Settings checkboxes are now clearly legible in Dark and High Contrast themes.

## 1.7.0 - 2026-06-21

### Changed
- **Brand-new "Clinical" ribbon interface.** The toolbar is now organized like familiar office apps — **View / Edit / Pages / Sign** tabs open clearly labeled groups of tools — with a smooth animated transition when switching tabs. A **Quick Access toolbar** in the title bar keeps Open, Save, Print, and Undo one click away, and zoom moved to the bottom-right of the status bar.
- A clean light theme with a precise red accent (the **Light + Red "Clinical"** look) is now the default; the design carries across every theme (Light, Dark, High Contrast) and accent.
- Refreshed app icon and Microsoft Store artwork to match — a steel tile with the scalpel's surgical-red cutting edge.

## 1.6.0 - 2026-06-17

### Added
- **Tools menu — six local-only PDF power features** (no subscription, no uploads), exposed via a new **Tools ▾** button in the toolbar's File group. Each is backed by a unit-tested service under `Services/`:
  - **Page numbering / Bates numbering / headers & footers** (`BatesNumberingService`) — sequential Bates IDs with prefix/suffix and zero-padding, `Page X of N` page numbers, or custom header/footer text, placed in any of six page corners.
  - **Compress PDF** (`PdfCompressionService` + `DocnetPageRasterizer`) — Low/Medium/High presets re-encode pages as downscaled JPEGs entirely on-device; highly effective on scan/photo-heavy files. Output pages become images (text non-selectable) — run OCR after to restore searchable text.
  - **Make Searchable (OCR)** (`OcrService`, `SearchableLayerWriter`, `TesseractTsv`, `TesseractCliOcrEngine`, `OcrAssets`) — turns scanned pages into selectable, searchable text via an invisible text layer, fully offline. Distribution-aware packaging: the installed Windows download bundles the Tesseract engine + English data in `<AppDir>\ocr`; the portable build fetches the language data on demand (~12 MB, official tessdata source) into `%LOCALAPPDATA%\Scalpel\ocr`.
  - **Redact Marked Areas** (`RedactionService`) — mark regions with the Highlight tool, then permanently flatten the affected pages to images with black boxes so the underlying text is unrecoverable (not just visually covered). Untouched pages keep selectable text.
  - **Password Protect** (`PdfEncryptionService`) — encrypt a saved copy with a user password and print/copy permission flags. (Removing a known password is implicit: open it, then Save As.)
  - **Remove Metadata** (`MetadataSanitizer`) — strip author, title, subject, keywords, and the hidden XMP metadata stream before sharing.
- The self-installer now carries a bundled `ocr/` folder (when shipped next to the EXE) into the install directory, so the installed "Windows (with OCR)" download works offline out of the box; `release.ps1` gains an `-OcrSourceDir` parameter that assembles that distribution.
- Local-only session logging for diagnostics and QA. Each app launch writes a JSONL log to `%LOCALAPPDATA%\Scalpel\logs\` (one file per session, named `scalpel-<timestamp>.jsonl`) recording app start/exit, every button and menu click, the outcome of major operations (open, save, save-flattened, merge, extract, sign, print), and all errors and crashes. On by default, with a **Settings → Diagnostics** section to toggle logging, open the logs folder, or clear old logs; session logs older than 7 days are removed automatically on startup. Stays fully offline — logs are written only to the local machine and never transmitted. See `docs/LOGGING.md`.

### Fixed
- Opening the Settings panel no longer records a spurious `logging.toggle` event or re-saves the logging setting on every open; the Diagnostics checkbox now syncs to the current state without re-triggering its change handler.

## 1.5.1 - 2026-06-14

### Fixed
- PDFs that opened fine in browsers and Acrobat/Foxit but failed in Scalpel with "Unexpected EOF" now open. PdfSharpCore rejected them during parsing; Scalpel now falls back to re-saving the file losslessly through PDFium (which reads them) and opening that copy (Issue #72).
- Files opened from UNC / network shares (including the WSL `\\wsl$` filesystem) are now copied to a local temp before opening, avoiding partial-read failures on network filesystems.
- Grid view now renders every page, and tiles stream in progressively as they render instead of blocking until the whole document is done. Grid was previously capped at the first 26 pages, so longer documents stopped loading partway through.
- Ctrl+Scroll in grid view no longer re-renders every page when the zoom is already at its limit (the column count cannot change), which made large documents reload pointlessly.
- Lowered the minimum zoom from 10% to 5% so grid view can pack more columns (useful for wide/landscape pages) and single-page view can zoom out further.
- Removed a stray horizontal scrollbar (a thin green line) that appeared across the bottom of grid view; grid fits its columns to the window and no longer scrolls sideways.

### Changed
- Save Flattened PDF now rasterizes across multiple CPU cores. PNG encoding runs in parallel; the PDFium render step is serialized because the library is not thread-safe. Large documents flatten faster and the UI stays responsive (Issue #68).

## 1.5.0 - 2026-06-14

### Added
- Localization support (Issue #53 / contributor leox243). Language selector in Settings panel. Ships with English (en-US), Spanish (es), and Traditional Chinese (zh-TW). Theme names, zoom dropdown, fit-mode status, and keyboard shortcut overlay all update with the selected language. Contributor guide at `Strings/TRANSLATING.md`.
- Continuous scroll view mode. Opens all pages in a single vertical strip with progressive async rendering. Page number and sidebar thumbnail track automatically as you scroll.
- Two-page view mode. Displays two pages side-by-side (primary + one secondary). Editing tools are available in this mode.
- Re-edit placed text by double-clicking it with the Select tool. The text re-opens with its current content, size, and color; the size dropdown and color swatches restyle it live while editing.
- Per-monitor DPI v2 support. Window and page re-render correctly when dragging between monitors with different scale factors.
- Zoom +/− toolbar buttons and keyboard shortcuts (Ctrl+=, Ctrl+−, Ctrl+0, Ctrl+Scroll).
- Crop tool improvements (Issue #15): editable CropBox coordinates, page range apply, TrimBox sync, rotation-aware coordinate conversion, draggable confirm bar.
- Settings persistence - window size, zoom, and fit mode saved/restored on launch (Issue #69).
- Global crash handler with structured log files and recovery dialog.
- About dialog (click the version label in the status bar).
- Authenticode install gate, downgrade protection, and pdfium.dll integrity check.
- Theme system: Dark, Light, High Contrast, Blood, Greed, and Cyanotic themes with live switching and settings panel (gear icon)
- Grid view zoom fits a whole number of pages across the window. Ctrl+Scroll steps through column counts (3, 4, 5 and up) and the grid opens at three pages across.
- Built-in print dialog with working print preview. Replaces the Windows print dialog (which showed "This app doesn't support print preview") with a themed dialog that previews each page and exposes printer, orientation, copies, and page-range (for example 1-3,5) settings.

### Changed
- Continuous scroll is now the default view mode for new installs.
- View mode order in Settings: Continuous, Single Page, Two-Page, Grid.
- Settings and keyboard shortcut overlay borders widened to 2px for better visibility.
- Text tool size value is now interpreted as points. A size of 14 renders and exports as roughly 14pt instead of about 5pt of internal render units.
- Placing an image now switches to the Select tool with the image selected, so you can immediately drag to reposition or use the corner handle to resize instead of the next click reopening the image picker (matching signature placement).
- Extracted SignatureStore and SearchService into Services/ with unit tests (Scalpel.Tests).
- Encrypted PDF temp files written to `%LOCALAPPDATA%\Scalpel\Temp\` instead of `%TEMP%`.
- Reopens last file on startup; ESC closes the app when no overlay is active (Issue #69).
- Grid view mode moved from a toolbar toggle to the Settings panel alongside Theme and Language. Four modes: Single Page, Continuous, Two-Page, Grid. Selection persists across sessions.
- Switching to Single or Two-Page view fits the page to the window, Continuous opens fit-to-width, and Grid opens at its column-fit default, rather than carrying the previous mode's zoom level.
- Annotation toolbars (text and draw size/color) now appear at the top-right under the toolbar buttons instead of the top-left.
- Four corner resize handles on placed images and signatures. Drag any corner to resize with the opposite corner held fixed. Handles are larger and render at the same on-screen size in every view mode.

### Fixed
- Stale debug string appearing in status bar after Fit Width in single-page mode.
- Text edit box closed when changing the font size, because the size dropdown took keyboard focus and triggered a commit. Focus moving into the size or color bar no longer commits the edit.
- Crop confirm bar was scaled down with page zoom, making it unreadable at low zoom levels. Selection rectangle improvements.
- Save Flattened PDF now runs on a background thread (Issue #68).
- Cropped pages rasterize at CropBox size instead of document-wide maximum (Issue #68).
- Temp files cleaned up on close, crash, and startup.
- Undo of a document change (crop, rotate, page operations) now re-renders the active view, so a page no longer keeps showing its pre-undo state while the sidebar shows the correct version.

## 1.4.3 - 2026-06-08

### Fixed
- Encrypted PDFs (owner-restricted RC4) no longer fail with "Unexpected token 'xref'" when rotating pages. PdfSharpCore can silently produce a broken cross-reference entry after saving encrypted files; Scalpel now pipes the file through PDFium to repair the XRef and retries the open automatically.
- Page view now fits to page after a rotation so the full rotated page is visible without manual rezoom.
- Mailto and other link annotations with visible borders (e.g. colored rectangles that looked like strikethroughs) no longer render those borders in saved PDFs. Scalpel strips `/AP`, `/C`, and `/BS` from link annotations and sets an invisible border on save.
- Right-click a link annotation to remove it from the PDF entirely ("Remove Link from PDF"). Previously, clearing annotations only removed the Scalpel overlay; the native PDF link remained active.
- Right-click a mailto link to copy just the email address; right-click an http/https link to copy the URL.

## 1.4.2 - 2026-06-06

### Added
- PDF form filling. Interactive PDF forms now render their fields (text inputs, checkboxes, radio buttons) as live controls. Fill them in directly and save — field values are written back into the PDF.
- PDF outline (bookmark) support (Issue #63). A new OUTLINES tab in the sidebar displays the document's bookmark tree. Click any entry to jump to that page. The sidebar auto-fits its width to the longest entry on open and can be dragged wider; switching back to PAGES snaps to the pages-mode width.

### Fixed
- Page rotation no longer reverts after saving. Rotations applied via the sidebar context menu now persist correctly through the save pipeline.
- Copied text words were out of order on PDFs where glyphs are stored in non-reading order (Issue #66). Text extraction now sorts words by position and uses a dynamic line-grouping threshold so both drag-select and Select All produce correctly ordered output.
- PDFs with malformed or non-standard XRef tables now open in read-only mode instead of showing "Invalid entry in XRef table" and failing entirely.

## 1.4.1 - 2026-05-21

### Added
- Page number jump box in toolbar. Type a page number and press Enter to navigate directly to that page.
- Signature auto-selects after placing so you can immediately reposition or resize without switching tools.
- Zoom to Width / Fit Page now re-applies when the window is resized.
- Middle mouse button panning. Hold middle mouse and drag to pan the view in any direction.
- Multi-page grid view toggle (toolbar button left of the zoom dropdown). Switch between seeing all pages in a scrollable grid and a focused single-page view. Defaults to grid view on open.
- Ctrl+S saves directly to the current file without a dialog. Ctrl+Shift+S opens Save As.
- Arrow key navigation: Left/Up goes to the previous page, Right/Down goes to the next page.
- Keyboard shortcut overlay. Press Ctrl+? to show a full shortcut reference. Dismiss with Escape or by clicking outside the panel.
- Crop tool improvements: corner drag handles to resize the selection after drawing without having to redraw; Enter applies the crop to the current page; Escape cancels; Remove Crop / Remove All buttons in the confirm bar clear an existing CropBox from one page or all pages.

### Fixed
- Fit to Width and Fit Page zoomed incorrectly on HiDPI (4K) displays.
- Pages appeared blurry at higher zoom levels on HiDPI displays.
- Signature position drifted after saving.
- Memory spike (6+ GB) when opening large PDFs on HiDPI displays.
- Navigating pages caused multi-second UI lag on documents with many pages.
- Scroll wheel now navigates to the previous page when scrolled to the top of a page, and to the next page when scrolled to the bottom.

## 1.4.0 - 2026-05-16

### Added
- Rotate page (Issue #52). Right-click any page in the sidebar to rotate it 90° clockwise or counter-clockwise. Works on multi-page selections.
- Insert Image tool (Issue #50). Click the toolbar button, then click anywhere on the page to place a PNG, JPG, BMP, GIF, or TIFF as a resizable annotation. Drag the green corner handle to resize; burned into the PDF on save.
- PDF link annotation support (Issue #47). Clicking hyperlinks and internal cross-references in a PDF now navigates to the target page or opens the URL in the default browser. Works on both the primary page and all secondary pages in multi-page grid view.
- New Blank Document (Ctrl+N, toolbar button). Creates a single blank A4 page as a new working document. Prompts to discard unsaved changes if a dirty file is open.
- Typewriter tool font size picker. When the Text tool is active, a settings bar appears showing size presets (8–72pt) and a color palette. Size and color are stored per-annotation and applied when flattening to PDF.
- Insert Blank Page. Right-clicking any page in the sidebar now shows a context menu with page-level operations: insert a blank A4 page, move up/down, extract, or delete.
- Signature resize. Placed signatures now show a green drag handle in the bottom-right corner. Dragging it scales the signature proportionally; releasing commits the new size.
- Multi-page grid view. When viewing a page, subsequent pages render as a tiled grid to the right and below, allowing context across multiple pages at once.
- Fit to Width on open. Files now auto-zoom to fill the viewer width on open instead of opening at 100% and clipping wide pages.

### Fixed
- Scroll wheel in the main viewer no longer triggers page navigation. Previously, at low zoom levels where the page fit entirely in the viewport, every scroll tick caused a full page re-render.
- Page selection no longer flashes centered before jerking left. The layout width is now managed exclusively in the Dispatcher callback, eliminating the double layout pass that caused the visual artifact.
- "Back to TOC" and other internal links on secondary pages now navigate to the correct target instead of advancing to the next sequential page.
- Clicking an internal link now scrolls the viewer back to the top of the target page so links pointing to page tops (e.g. TOC back-links) land correctly.
- Internal PDF links now survive a merge. When merging PDFs, named destinations from the source document's catalog are resolved and rewritten as explicit page-object references in the merged document, so TOC and cross-reference links continue to work after merging.
- Multi-page grid content is now centered in the viewport instead of left-aligned. Panel width is snapped to a whole number of page-width slots so HorizontalAlignment=Center has room to work.
- Sidebar page list no longer shows empty space after the last page. The list now ends at the final page entry with no trailing dead zone.

### Changed
- Theme updated to match scalpel.example.com: accent green changed from `#4ade80` to `#1ea54c`, backgrounds shifted to `#333333`/`#3a3a3a`, sidebar darkened to `#222222`, toolbar and title bar at `#222222`. Film grain overlay added to the main content area. Footer text lightened for readability.
- Sidebar scroll is now handled by an outer ScrollViewer wrapping the page list, allowing the list to size to its content rather than stretching to fill the panel height.

## 1.3.2 - 2026-05-11

### Fixed
- Windows Program Compatibility Assistant popup on first launch. Added an app manifest declaring Windows 10/11 compatibility, which suppresses PCA when the app writes to uninstall registry keys.
- "Set as default PDF viewer" prompt now only appears if Scalpel is not already the default handler. Previously showed on every install/update regardless.
- "Set as default PDF viewer" prompt now uses the dark KillerDialog instead of a native Windows message box.

## 1.3.1 - 2026-05-11

### Fixed
- Print no longer fails with "No application is associated with the specified file for this action" on systems where Edge is the default PDF handler. Printing now uses WPF-native rendering and PrintDialog instead of the shell print verb.
- Zoom dropdown selected value no longer shows in blue - selection highlight now uses the accent green.

## 1.3.0 - 2026-05-08

### Added
- Image signatures. Import a PNG, JPG, or BMP as a reusable signature instead of drawing one. Stored alongside drawn signatures and flattens into the PDF on save.
- Close File (Ctrl+W). Close the current document without quitting the app. Prompts if there are unsaved changes.
- Unsaved-changes protection. The title bar marks dirty files with `*` and prompts before closing or opening a new file with unsaved edits.
- Full-document Find. Ctrl+F search now scans the entire PDF and cycles through all matches, not just the current page.
- Zoom preset dropdown with quick presets (50%, 75%, 100%, 125%, 150%, 200%). Scroll-wheel zoom syncs the box, including non-preset levels.

### Fixed
- Scrolling past the bottom of a page now advances to the next page; scrolling past the top goes back.
- Re-dropping a PDF onto the window after a file is already open now works correctly.
- Owner-password-protected PDFs now open correctly (previously only user-password was handled).
- Dragging the title bar while maximized now correctly restores and moves the window.
- Delete confirmation now reads "Delete 1 page?" or "Delete 2 pages?" instead of "Delete N page(s)?".
- Signature delete button showed a rectangle glyph instead of an X.

### Changed
- All dialog boxes are now fully dark-themed via a custom dialog window. No more native Windows popups.
- Create Signature dialog now uses a dark custom chrome title bar with a red X close button.
- Button hover states and page thumbnail hover in the sidebar are now green instead of the default Windows blue.
- Toolbar icons overhauled: Open Folder, Close File, Move Up, Move Down, Extract Pages, and Merge PDFs all use cleaner glyphs.

## 1.2.1 - 2026-05-04

### Changed
- Code signed with Certum certificate. Windows now shows a verified publisher instead of unknown.
- Cleaned up footer.

## 1.2.0 - 2026-04-24

### Added
- Self-installing EXE. Running the downloaded binary now shows an Install / Run dialog. Install copies the EXE to `%LOCALAPPDATA%\Programs\Scalpel\` (no UAC required), creates Start Menu and optional Desktop shortcuts, registers as a PDF file handler, and adds an uninstall entry to Add/Remove Programs. Uninstall self-deletes via a deferred batch file. Running a newer version from outside the install path shows an Update prompt instead.
- Command-line file argument support so file associations work: `Scalpel.exe "file.pdf"` opens the file directly.
- Password-protected PDF support. Opening an encrypted PDF now prompts for the password instead of showing a generic error. The decrypted copy is held in a temp file for the session so all rendering and editing works normally.
- Save Flattened PDF (photo icon in toolbar). Rasterizes every page at 150 DPI via PDFium and writes them as embedded images into a new PDF, producing a fully uneditable document. Pending annotations are burned in before rasterization.

## 1.1.1 - 2026-04-18

### Fixed
- Maximize no longer covers the Windows taskbar. Added a `WM_GETMINMAXINFO` hook so the frameless window clamps to the monitor's work area (multi-monitor aware).
- Two `CS8602` nullability warnings in the font-name cleanup path.

## 1.1.0 - 2026-04-16

### Changed
- Retargeted from .NET 8 to .NET Framework 4.8 so end users no longer need to install a separate .NET runtime.
- Forced 64-bit build via `PlatformTarget=x64`.
- Added PolySharp polyfills for modern C# language features on net48.
- Replaced `Math.Clamp` calls with `Math.Min`/`Math.Max` equivalents.

### Added
- Post-publish MSBuild target that automatically bundles a GPL3-compliant source zip alongside the published EXE.
- CHANGELOG.md.

