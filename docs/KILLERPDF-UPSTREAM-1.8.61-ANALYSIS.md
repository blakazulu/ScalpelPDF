# KillerPDF upstream analysis: 1.8.1 to 1.8.61

Continues `docs/KILLERPDF-UPSTREAM-1.8.1-ANALYSIS.md` (1.6.0 to 1.8.1, plus its 1.8.2/1.8.3
addendum). The reference dump under `docs/github-origin/` is still **v1.8.1**; upstream's newest
tag on <https://github.com/SteveTheKiller/KillerPDF> is **v1.8.61** (2026-09-22).

Checked 2026-09-22. Sources: `git log v1.8.1..v1.8.61` (194 commits, 240 files,
+13,801 / -1,915), the diff of every app and engine commit, the CHANGELOG / GitHub release notes
for 1.8.2, 1.8.3, 1.8.4, 1.8.5, 1.8.6 and 1.8.61, and a grep of the Scalpel tree for every
"applies" and "already in Scalpel" verdict.

---

## 1. Executive summary

| Release | Date | Commits | Upstream headline |
|---|---|---|---|
| 1.8.2 | 2026-08-31 | 67 (with 1.8.3) | Published PDF corpus + benchmark; Office hybrid-xref PDFs open; opaque text-edit covers; 1-bit Transform output |
| 1.8.3 | 2026-09-02 | | Footer page size, pixel dimensions for DPI, first launch follows Windows language, portable data folder, Save Flattened 1-bit |
| 1.8.4 | 2026-09-06 | 50 | Tab-close crash (#353), wrong first page after switching (#378/#379), unsaved annotations kept on blank-page insert (#388), JPEG kept when flattening (#366), resize scroll drift (#373), monitor maximize (#363), hardcoded-English gate (#227) |
| 1.8.5 | 2026-09-14 | 77 (with 1.8.6, 1.8.61) | Startup update checks, Ctrl+R rotate, shortcut on/off toggle (Ctrl+Shift+K), blank-page size dialog, per-tab scroll/zoom (#399), Open Containing Folder on tabs, `--batch-render` |
| 1.8.6 | 2026-09-22 | | Annotations kept when render dims are missing, annotations aligned on cropped pages (#418), Vietnamese, display restore after minimize (#415) |
| 1.8.61 | 2026-09-22 | | Stale `AssemblyVersion` broke the installer; build now asserts version equality |

Structural notes:

- Upstream now runs a **1.8.x maintenance line** (`main`) and a **1.9 "Overkill"** development
  branch, with `.github/maintenance-forward-ports.json` recording which fixes were carried over.
- The engine keeps growing (validation messages localized, tagged-PDF overlay guards, page-size
  normalizer feasibility check, signing into existing fields). None of it is portable to net48;
  only the behaviours are, and each one is mapped in the appendices.
- Upstream's own **document tabs** matured in this range (#353, #378/#379, #399). Scalpel's tab
  strip is a path switcher, so those fixes are folded into the multi-document tabs plan
  (`docs/superpowers/plans/2026-09-22-chrome-tabs.md`) instead of being ported one by one.

### Verdict totals (all three ranges)

| Verdict | 1.8.2-1.8.3 | 1.8.4 | 1.8.5-1.8.61 |
|---|---|---|---|
| Applies - port | 11 | 16 (10 distinct) | 17 (11 distinct) |
| Already in Scalpel | 3 | 8 | 5 |
| Already triaged (1.8.1 addendum) | 14 | - | - |
| Needs verification | 1 | 2 | 6 |
| Not applicable | 16 | 28 | 16 + engine/web/CI rows |

---

## 2. Prioritized port backlog

The distinct work items after de-duplicating the three ranges. IDs point into the appendices
(`A` = 1.8.2-1.8.3, `B` = 1.8.4, `C` = 1.8.5-1.8.61). Effort: S = under half a day, M = one to
two days, L = more.

### Wave 1 - data loss and save integrity (do first)

| # | Item | Upstream | Scalpel location | Effort |
|---|---|---|---|---|
| P1 | Save Flattened clears the dirty flag after exporting to ANOTHER file, so closing loses every unsaved annotation | B B8 (#356) | `MainWindow.FileToolbar.cs:696` `MarkDirty(false)` | S |
| P2 | Annotations on a page are silently dropped at save when `_renderDims` has no entry for it; fall back to canonical 2048-long-edge dims | C B5 | `MainWindow.SaveAnnotations.cs:104` | S |
| P3 | Page operations (insert blank, move up/down, delete, merge, drag reorder, link ops) drop ALL unsaved annotations; remap annotation page keys and reload with `keepAnnotations: true` | B B4 (#388, broader in Scalpel) | `MainWindow.TempReload.cs:78`, callers in `FileToolbar.cs`, `DragDrop.cs:92`, `Links.cs:186` | M |
| P4 | Atomic save: `PdfSaveGuard.Save` writes over the user's file and rewrites it in place; a failure mid-way truncates the original | C B8 / O3 | `Services/PdfSaveGuard.cs` | S |
| P5 | Page-size normalizer makes an infeasible page worse (20000 x 2 pt becomes 14400 x 1.44); return 1.0 when no uniform scale fits | C B19 (#401) | `Services/PdfSaveGuard.cs:186` | S |
| P6 | Annotations misplaced on cropped pages at save and print (burn ignores the CropBox origin) - repro first | C B4 (#418) | `MainWindow.SaveAnnotations.cs:133-140`, `Services/BurnRotation.cs` | M |
| P7 | Transform (flip / fine angle / scale) drops document info and every bookmark | A B8b | `Services/TransformService.cs` `ApplyRasterized` | M |

### Wave 2 - file size and fidelity

| # | Item | Upstream | Scalpel location | Effort |
|---|---|---|---|---|
| P8 | Keep JPEG when flattening / repairing pages whose images are all DCT | B B10 (#366) | `MainWindow.FileToolbar.cs:669/766`, `MainWindow.FileOps.cs:575` | M |
| P9 | Save Flattened stores bitonal pages as 1-bit and gray pages as 8-bit gray | A B7 (#323) | `MainWindow.FileToolbar.cs` `SaveFlattened_Click` | M |
| P10 | Flatten of rotated PDFs keeps orientation - repro first | B B9 (#362) | `MainWindow.FileToolbar.cs:621-627` | S-M |
| P11 | Fractional font sizes survive re-edit; size combo shows `0.##` | B B5 (#356) | `MainWindow.InlineTextEdit.cs:121/287`, `MainWindow.TextBar.cs:71` | S |
| P12 | Extraction ignores malformed `/Thumb` images - repro first | C B6 (#423) | split/extract in `MainWindow.FileToolbar.cs` | S |

### Wave 3 - window and view behaviour

| # | Item | Upstream | Scalpel location | Effort |
|---|---|---|---|---|
| P13 | Maximize after moving to a larger monitor (`ptMaxTrackSize` clamp) | B B14 / C B11 (#363) | `MainWindow.Settings.cs:307-308` | S |
| P14 | Continuous view drifts on window resize; scale `VerticalOffset` by the zoom ratio | B B15 (#373) | `MainWindow.Zoom.cs` `FitToWidth`/`FitToPage` | S |
| P15 | Fit Width in Continuous keeps a stale horizontal offset - repro first | C B3 | `MainWindow.Zoom.cs` | S |
| P16 | Zoom box clips localized Fit labels; measure and clamp 70-220 px | C B12 (#419) | `MainWindow.xaml:1050` | S |
| P17 | Windows 11 taskbar icon after slow cold start - verify first | C B10 | `MainWindow.xaml` | S |

### Wave 4 - features

| # | Item | Upstream | Effort |
|---|---|---|---|
| P18 | Ctrl+R / Ctrl+Shift+R rotate selected pages | C F2 | S |
| P19 | Keyboard shortcut on/off toggle, Ctrl+Shift+K always works (Scalpel has single-letter tool keys) | C F3 (#405) | S-M |
| P20 | Blank page size dialog (Current / Letter / A4 / Legal / Custom) instead of hardcoded A4 | C F4 (#400) | S-M |
| P21 | First launch follows the Windows display language (incl. he/ar RTL) | A F4 (#322) | S |
| P22 | Page-size readout in the status bar, clickable unit cycle (px / in / mm / pt) | A F1, B F1 (#301/#364) | S-M |
| P23 | Output pixel dimensions (and scale) for the chosen DPI in Export Images | A F3, B F2 (#310/#365) | S |
| P24 | `EstimatedSize` in the Uninstall key + Company/Copyright/Product file details | B B28 (#361) | S |
| P25 | Form fields show their own `/MK /BC` border colour (design choice - replaces the green cue) | A F11 | S |
| P26 | `--batch-render` developer CLI for render timing (optional) | C F6 | M |

Tab-strip items (#353, #378/#379, #399 per-tab scroll/zoom, tab context menu with Open Containing
Folder, stale thumbnails after switch, last-tab cancel bug) are all covered by the tabs plan.

### Wave 5 - localization and release hygiene

| # | Item | Upstream | Effort |
|---|---|---|---|
| P27 | Localize the ~100 hardcoded English strings (status lines, dialogs, titles, 19 context-menu items, installer UI, password prompt, crop bar) | A B13, B B30, C F8 (#227) | M-L |
| P28 | A hardcoded-UI-text gate in `LocaleParityTests` with a shrinking allow-list | B B30, C O4 | S-M |
| P29 | Install errors use themed dialogs instead of `MessageBox.Show` | C B15 | S |
| P30 | `release.ps1`: git-sync gate, `dotnet test` gate, EXE FileVersion equals csproj Version | A O10/O11, C B17 | S |
| P31 | Optional: local real-world corpus open/save runner with per-file CSV diff between versions | A O9, C O7 | M |
| P32 | Optional: an `upstream-ledger.json` mapping each upstream SHA to a Scalpel verdict so the next triage is incremental | C O6 | S |

### Open product decisions (not started without an answer)

- **#327 portable data beside the EXE** instead of `%LOCALAPPDATA%\Scalpel` (carried over from the
  1.8.1 addendum; needs a migration).
- **P25 form-field border colour** replaces Scalpel's green "fillable" cue.
- **Refresh `docs/github-origin` to v1.8.61** so later ports can read the current upstream tree
  directly (the dump is untracked; a straight replace).

---

## 3. Scalpel-only defects found during this triage

Not upstream items; found while checking upstream fixes against Scalpel code.

| Defect | Where | Handled by |
|---|---|---|
| Open, drag-drop (including onto an open document) and Recent replace a dirty document with no save prompt | `MainWindow.FileToolbar.cs:73`, `MainWindow.DragDrop.cs:35`, `MainWindow.Recent.cs:41` | Tabs plan Task 1 |
| `FinishOpenFile` and `CloseFile` do not clear `_redoStack`, so Redo can replay into another document | `MainWindow.FileOps.cs:627`, `MainWindow.DirtyTracking.cs` | Tabs plan Task 1 |
| `LastFile` stores the working copy path, so it stops updating after any page edit | `MainWindow.Settings.cs:386-389` | Tabs plan Task 1 |
| Save As leaves the tab pointing at the old path | `MainWindow.FileToolbar.cs:559/567` | Tabs plan (session identity) |
| Sidebar shows the previous document's thumbnails after a tab switch | `MainWindow.FileOps.cs:826-841` | Tabs plan Task 5 |
| Cancelling the dirty prompt on the last tab drops the tab entry | `MainWindow.Tabs.cs:157-162` | Tabs plan (Tabs.cs rewritten) |
| A failed open during a tab switch can leave the strip pointing at a closed document | `MainWindow.FileOps.cs:69-71` | Tabs plan Task 6 (open into a fresh session) |
| Compress / OCR / redact / straighten / form OCR write their result into whichever document is active after the `await` | `MainWindow.Tools.cs`, `MainWindow.FormOcr.cs`, `MainWindow.Straighten.cs` | Tabs plan Task 7 (busy gate + pinned session) |

---

## Appendices

The full per-commit triage for each range follows. Every row carries upstream commit SHAs, the
Scalpel file checked, the verdict, and for ports an effort and a net48/PdfSharpCore approach.


---

### Appendix A: Upstream KillerPDF v1.8.1..v1.8.3 - Scalpel port triage (part 1)

Range: `git log v1.8.1..v1.8.3` = 67 commits, 157 files (+4005 / -1109). Sources: every commit's
`--stat`, the diffs of every app/engine commit, and the CHANGELOG.md sections for 1.8.2
(2026-08-31) and 1.8.3 (2026-09-02). Prior triage reused from
`docs/KILLERPDF-UPSTREAM-1.8.1-ANALYSIS.md` (section 7 + "Addendum: upstream v1.8.2 and v1.8.3").

Scalpel checks were done by grepping `C:/Code/Personal/ScalpelPDF` (read-only). File names in the
Verdict column are Scalpel files.

Verdict key: `Applies - port` / `Already in Scalpel` / `Already triaged (see 1.8.1 analysis
addendum)` / `Not applicable (reason)` / `Needs verification`.

---

#### 1. New features (user-visible)

| # | Upstream change | Key upstream files / commits | Scalpel verdict |
|---|---|---|---|
| F1 | **Page-size readout in the footer.** Footer shows the current page's size as a named paper size when it matches (A0-A6, Letter, Legal, Tabloid/Ledger, ANSI C-E, 3 pt tolerance, orientation-agnostic) plus the primary unit (mm for ISO, in for US), with a tooltip giving in / mm / pt. New pure helper `PageSizeFormatter.Format(wPt, hPt)`. | `Services/PageSizeFormatter.cs`, `MainWindow.xaml`, `Shell/SidebarLayout.cs` - 8e7a563f (#301) | **Applies - port (S).** Scalpel's status bar (`MainWindow.xaml` `StatusBarBorder`) has `StatusText`, the portable badge, version and the zoom cluster, but no page-size label. Port `PageSizeFormatter` verbatim (pure C#, net48-safe; swap `double.IsFinite` if used) into `Services/`, add a `PageSizeLabel` TextBlock to the status bar, update it on page change from `_doc.Pages[i].Width/Height.Point` (respect `/Rotate`). Needs a unit test + a `Str_*` tooltip key in all 9 locale files. |
| F2 | **View-mode and zoom controls moved into the footer.** | `MainWindow.xaml`, `Shell/RailFlyouts.cs`, `Shell/SettingsPanel.cs` - 8e7a563f (#301) | **Already in Scalpel.** The Clinical redesign already relocated `ZoomBox` / `ZoomOutBtn` / `ZoomInBtn` into the status bar (`MainWindow.xaml` ~L1049-1063). View modes live in the ribbon's View > Layout group by design. |
| F3 | **Output pixel dimensions shown for the chosen DPI** in Export Images and Transform (e.g. "1275 x 1650 px"), via a new `OutputPixelDimensions.FromPoints(wPt, hPt, dpi)` helper. | `Services/OutputPixelDimensions.cs`, `Controls/ExportImagesDialog.cs`, `Controls/TransformWindow.cs`, `Shell/FileOperations.cs` - 78597c9a (#310) | **Applies - port (S).** Scalpel's export (`MainWindow.ExportImages.cs`, `ToolsExportImages_Click`) uses a `ShowToolForm` with a fixed 72/150/300/600 DPI combo and computes `longEdgePx` only after the dialog closes. Port the 10-line helper and show the pixel size in the form note (or live, if `ToolField` gains a change callback). Note Scalpel clamps the long edge to 200..10000 px, so the display must apply the same clamp. |
| F4 | **First launch follows the Windows display language** (`CultureInfo.CurrentUICulture` mapped to a supported locale, zh-Hant/TW/HK/MO -> zh-TW, else zh-CN, unknown -> en-US), and the choice is persisted immediately. | `Services/LocaleManager.cs` - f1bac786 (#322) | **Applies - port (S).** Verified: `Services/LocaleManager.cs` `Initialize()` still does `Enum.TryParse(saved) ? l : Locale.EnUS`. Port `LocaleForCulture` mapped onto Scalpel's enum `{EnUS, Es, ZhTW, ZhCN, Bn, TrTR, He, Ar, Ru}` (add `he`, `ar` - which also flip RTL via `IsRtlLocale`). net48 has `CultureInfo.CurrentUICulture`; replace the `string.Contains(..., StringComparison)` overload (not on net48) with `IndexOf(...) >= 0`. Add a unit test on the pure mapper. |
| F5 | **File-picker sorting** (name / size / modified, remembered) and full-name tooltips. | `Controls/FileDialog.xaml.cs` - f8ae7600, 9a6212c2 | **Already triaged (see 1.8.1 analysis addendum).** Not applicable: Scalpel uses the OS file dialog. |
| F6 | **Portable data beside the launcher** (`KillerPDF-Data\settings.json`, signatures, swatches, tessdata) when started from the portable launcher; JSON settings replace registry in that mode. | `Services/AppDataPaths.cs`, `App.xaml.cs`, `Services/SignatureStore.cs`, `Services/OcrNativeBootstrap.cs`, `KillerPDF.Tests/AppDataPathsTests.cs` - 21964e51 (#327) | **Already triaged (see 1.8.1 analysis addendum)** - left as an open product decision. Extra detail for whoever decides: upstream keys it off a `KILLERPDF_LAUNCHER_PATH` env var set by its launcher; Scalpel has no launcher, so it would key off `App.IsPortable()` and would need a JSON shim behind `App.GetSetting/SetSetting` (registry today), plus `SignatureStore` and `OcrAssets` path changes and a migration. Est. M if approved. |
| F7 | **Crash report window redesign**: themed chrome, a summary card (exception type, message, log path), exception chain text, Copy Report / Open Logs buttons, and a developer-only `--crash-dialog-preview` switch that shows the dialog with a synthetic exception. | `App.xaml.cs`, `Controls/DialogChrome.cs` - 4784adda | **Already triaged (see 1.8.1 analysis addendum)** as upstream chrome. Verified Scalpel `App.xaml.cs` `ShowCrashDialog` already has Copy Report + Open Logs (L538-545). The one reusable idea is the `--crash-dialog-preview` switch (S) for testing the dialog without crashing; optional. |
| F8 | **Corpus page and benchmark results on the website**. | `pdf-landing/corpus*.html` - 6502bcf7, 4450417a, 12a2579c, d70a4278, 9ed44ea4 ... | **Not applicable** (upstream marketing site). See O9 for the underlying test-infrastructure idea. |
| F9 | **Contributor guide** (code, docs, translations). | `CONTRIBUTING.md` - e42afe8e | **Not applicable** (Scalpel is single-maintainer; `Strings/TRANSLATING.md` already covers translators). |
| F10 | **New fillable fields start with a font size suited to their height** (`clamp(h * 0.5, 12, 24)`); creating/filling a field no longer rebuilds the view. | `Services/PdfEngineIntegration.cs`, `Controls/Viewer/PdfViewer.FormAuthoring.cs` - 2d529190 | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel fills fields, it does not create them. |
| F11 | **Form fields show their own border colour** (widget `/MK /BC`) at rest instead of no outline; focus switches to accent. | `Controls/Viewer/PdfViewer.Forms.cs`, `engine/.../PdfFormWidgetReader.cs` (`BorderColor`) - 2d529190 | **Applies - port (S, low priority).** Scalpel's overlays (`MainWindow.Forms.cs` L110-128, L183, `BuildCombField`) use a fixed `greenBrush` border. Reading `/MK /BC` via PdfSharpCore (`widget.Elements.GetDictionary("/MK")?.Elements.GetArray("/BC")`, 1/3/4 components) and using it as the resting border is easy; whether to drop Scalpel's green "this is fillable" cue is a design call - present as an option. |

#### 2. Bug fixes

| # | Upstream fix | Key upstream files / commits | Scalpel verdict |
|---|---|---|---|
| B1 | **Microsoft Office hybrid-reference PDFs rejected** as damaged (companion xref stream `/Size` one below trailer; generation-65535 retired entries). | `engine/.../PdfCrossReferenceTable.cs` - 24774de7, merge 8c5f46c7 (#314) | **Already triaged (see 1.8.1 analysis addendum)** - not applicable; guarded by `Scalpel.Tests/HybridXrefOpenTests.cs` (verified present). |
| B2 | **Images flipped vertically when burning annotations** (image drawn with positive height in a y-up space; fix draws at `y + h` with `-h`). | `Services/PdfEngineBurn.cs` - d70a4278 (#311) | **Already triaged (see 1.8.1 analysis addendum)** - guarded by `Scalpel.Tests/BurnImageOrientationTests.cs` (verified present). |
| B3 | **Context-menu zoom shortcut shown as `Ctrl+=`** instead of `Ctrl++`. | `Shell/ContextMenu.cs` - 54a207e7, merge c94b086c (#312) | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel's `Services/KeyLayout.cs` resolves the real character per keyboard layout (verified present). |
| B4 | **Text edits left the original text readable** - cover annotations were burned with Multiply blend, so a white cover did not hide text; now Normal blend for `CoverAnnotation`. | `Services/PdfEngineBurn.cs`, `KillerPDF.Tests/PdfBurnRotationTests.cs` (`CoverBurn_UsesNormalBlendSoItHidesOriginalText`) - d70a4278 | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel's white-out is opaque (`MainWindow.SaveAnnotations.cs` L231, no `/BM` anywhere in Scalpel) and it went further with `Services/ContentTextRemover.cs` + `Services/PdfToUnicodeMap.cs` (verified present). |
| B5 | **Fillable text fields selectable/movable with the Select tool** (1.8.2), then reverted to Form-Field tool only with no-move-no-commit click handling and restored Delete (1.8.3). | `Controls/Viewer/PdfViewer.Forms.cs`, `PdfViewer.Annotations.cs` - d70a4278, 2d529190, 24cf9b7c (#307) | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel has no field authoring. |
| B6 | **Black-and-white Transform output stored as 1-bit** (#305); **grayscale Transform output stored as one channel** (#324). | `Services/PdfEngineIntegration.cs` (`RasterPage.Bitonal`), `Shell/Rotate.cs`, `engine/.../PdfImage.cs` (`FromBitonal`) - d70a4278, 66e2a936 | **Already triaged (see 1.8.1 analysis addendum)** - applied to Scalpel's export path as `Gray8`/`Indexed1` in `Services/PageQualityConverter.cs` (verified L23-124). |
| B7 | **Save Flattened stores fully black-and-white pages as compact 1-bit images.** Detection = page's source images are all bitonal (engine hint `ReadBitonalImagePageHints`) AND the rendered BGRA is opaque and R=G=B everywhere (`BitonalPageDetector.IsOpaqueGrayscaleBgra`); then threshold at 128 and pack 1 bpp Flate. | `Services/BitonalPageDetector.cs`, `Services/PdfRasterize.cs`, `engine/.../PdfPageRasterInformation.cs`, tests `BitonalPageDetectorTests` - a6552779 (#323) | **Applies - port (M).** Verified: `MainWindow.FileToolbar.cs` `SaveFlattened_Click` (L630-696) renders every page to BGRA, encodes PNG, and draws it with `XImage.FromStream`, so a scanned B/W document is stored as 24-bit RGB Flate - several times larger than the source. Port: (1) hint = PdfSharpCore scan of each page's `/Resources /XObject` images for `/ImageMask true` or `/BitsPerComponent 1` (and no other images); (2) `IsOpaqueGrayscaleBgra` verbatim; (3) write the page image as a hand-built `PdfDictionary` XObject (`/Width /Height /BitsPerComponent 1 /ColorSpace /DeviceGray /Filter /FlateDecode`, deflate via `System.IO.Compression.DeflateStream` + zlib header) instead of `XImage`. Extend the same writer to 8-bit `/DeviceGray` for all-gray pages (3x smaller). Also benefits the print-prep path that reuses flatten. |
| B8 | **Transforming every page retained the superseded colour image resources** - now rebuilds from the rasterised pages only (`ReplaceAllPagesAndCompact`), re-applying document info (Title/Author/.../Trapped) and bookmarks sanitised to valid page indices (invalid parents dropped, valid children promoted, named destinations cleared). | `Services/PdfEngineIntegration.cs`, `Shell/Rotate.cs`, test `ReplaceAllPagesAndCompact_DoesNotRetainAnyOriginalPageImages`, `SanitizeRasterizedBookmarks_DropsInvalidParentsAndPromotesValidChildren` - d70a4278 | **Split verdict.** (a) Not retaining old images: **Already in Scalpel** - `Services/TransformService.cs` `ApplyRasterized` builds a fresh `PdfDocument` and adds only the new rasters, so nothing superseded survives. (b) Keeping metadata and bookmarks: **Applies - port (M).** The same fresh `outDoc` never copies `srcDoc.Info` or the outline, so a flip / fine-angle / scale Transform silently drops the title/author and every bookmark (the lossless quarter-turn path keeps them because it edits in Modify mode). Port: copy `srcDoc.Info.Elements` into `outDoc.Info`, and rebuild outlines from the source only when `Catalog.Elements.ContainsKey("/Outlines")` (per the CLAUDE.md dangling-xref gotcha), remapping destinations by page index and dropping ones that no longer resolve - upstream's sanitiser rules. Worth checking Save Flattened (same fresh-document pattern) at the same time. |
| B9 | **Transform no longer fails on a malformed bookmark tree** - retries with bookmarks cleared (`IsBookmarkGraphFailure`), dropping only the broken outline. | `Services/PdfEngineIntegration.cs` - d70a4278 | **Not applicable** today: Scalpel's rasterised Transform never reads outlines, so the failure cannot occur. Becomes relevant as soon as B8(b) is ported - wrap the outline copy in `try { } catch { }` and keep the pages (fold into B8, no separate effort). |
| B10 | **PDFs with empty unsigned signature values** (`/V ()` on a `/Sig` field, written by other tools) blocked save/export/sign; now treated as unsigned. | `engine/.../PdfSignatureReader.cs`, `PdfDetachedSignatureWriter.cs` + tests - 03f9d8da | **Not applicable.** Scalpel never interprets an existing `/V`: `Services/PdfSaveGuard.cs` `ClearSignatureField` removes any `/V` on a `/Sig` field regardless of type, and `Services/PdfSigningService.cs` always appends its own new `Signature1` field rather than filling an existing one. (Minor side note, not a port: a pre-existing field literally named `Signature1` would collide; worth a one-line uniqueness check some day.) |
| B11 | **Multi-column text highlighting put the caret in the wrong column** - line hit-test now breaks vertical ties on horizontal distance. | `Services/TextRunService.cs`, test `CaretUsesHorizontalPositionWhenColumnsShareTheSameLineHeight` - d70a4278 | **Not applicable.** Scalpel's text selection is a rectangle drag (`MainWindow.CanvasInteraction.cs` L388/591), not caret/flow selection; its `Services/TextRunService.cs` only orders words (`OrderColumnAware`). Keep this tie-break in mind if flowing selection (listed in section 7 of the 1.8.1 analysis) is ever ported. |
| B12 | **Comparison view**: continuous view on entry, same true zoom in both panes, both documents stay rendered when entering from another view mode, synced scroll/zoom, fit-to-pane-width on open, readable missing-page notices. | `Shell/PdfComparison.cs`, `Controls/Viewer/PdfViewer.Comparison.cs`, `PdfViewer.Viewport.cs`, `PdfViewer.Api.cs` - d70a4278, deb4de4c, d091fff6 (#301) | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel's compare is a page-by-page report (`MainWindow.Compare.cs`, `Services/PdfPageDifference.cs`). |
| B13 | **Remaining hard-coded status/error messages localised** (signature placement, annotation selection, page copy, file repair, form-field messages) (#227). | `Shell/Signing.cs`, `Controls/Viewer/PdfViewer.Annotations.cs`, `PdfViewer.Forms.cs`, `PdfViewer.FormAuthoring.cs`, `Shell/DragDrop.cs` - d70a4278, ada699cf | **Applies - port (M).** Same class of defect exists in Scalpel: a grep finds ~50 `SetStatus("...")`/`SetStatus($"...")` literals, ~39 `ScalpelDialog.Show(this, "...")` literals and ~10 hard-coded window `Title = "..."` across `MainWindow.*.cs` (e.g. `MainWindow.FileToolbar.cs` "Flattened PDF saved to", "Flatten failed"; `MainWindow.Selection.cs` L301; `MainWindow.Tools.cs` "Password Protect PDF"). Port = move each into `Str_St_*`/`Str_Err_*` keys in all 9 `Strings/*.xaml` and use `Loc(...)`/`string.Format`; `LocaleParityTests` already enforces key parity. Hebrew/Arabic users see English status lines today. |
| B14 | **Tool shortcut labels corrected** (#337: Stamp row label, "1 / 2 / 3", "1-6"; #339: added Fillable Text Field `F` to the shortcut reference, tooltip numeric alias only, Stamp tooltip `0`). | `Services/ShortcutTable.cs`, `Shell/KeyboardShortcuts.cs`, `KillerPDF.Tests/ShortcutTableTests.cs` - ccd8f84b, d53c3e9d | **Needs verification (S).** The specific rows are upstream's own table. Scalpel's F1 shortcut list is hand-written XAML (`MainWindow.xaml` ~L1300, e.g. `ZoomShortcutLabel`) and tooltips are separate, so the same drift is possible; audit the F1 list and ribbon tooltips against `MainWindow.KeyboardShortcuts.cs` bindings (number keys, letter keys V/T/H/D/L/I, Ctrl+1/2/3) once. |
| B15 | **Tab overflow chevron lists only hidden tabs** (was: every tab). | `Controls/Viewer/PdfViewer.TabStrip.cs` - 48fe4bfc | **Not applicable** - Scalpel's `MainWindow.Tabs.cs` `RefreshTabStrip` renders all chips and has no overflow menu. |
| B16 | **Annotation eraser naming** in the Highlight bar (#325). | `Shell/AnnotationBars.cs` - 196826c6 | **Already triaged (see 1.8.1 analysis addendum)** - upstream wording. |
| B17 | **Fillable fields in tagged PDFs** whose `/StructTreeRoot /K` holds several top-level elements (wraps them in a Document element before adding the field's struct element). | `engine/.../PdfIncrementalPageEditor.cs` + tests - 350f4dd6 | **Already triaged (see 1.8.1 analysis addendum)** - Scalpel does not create fields. |
| B18 | **Transform colour-mode list follows the theme; B/W threshold shows its value.** | `Controls/TransformWindow.cs` - d70a4278 | **Not applicable** - Scalpel's Transform (`MainWindow.Tools.cs`) is geometric only, no colour modes. |
| B19 | **Nested dialogs keep the theme grain texture** (walks the `Owner` chain). | `Controls/DialogChrome.cs`, `Controls/KillerDialog.cs` - d70a4278 | **Not applicable** - upstream grain/theme styling; Scalpel has no grain. |
| B20 | **Theme fixes**: Sepulchre radio ring/dot on hover, secondary-button hover colours, shared theme contrast (Delirium/Sepulchre), Light annotation bars opaque with white inputs, 98SE horizontal scrollbar. | `Services/ThemeManager.cs`, `Controls/UiKit.cs`, `Themes/*.xaml`, `App.xaml`, `Shell/AnnotationBars.cs`, `Shell/TextSettingsBar.cs` - fb0ed21c, 5d0da371, ba98495a, 4bb9dc65, 94d2aa5e | **Not applicable** - upstream's 13-theme set; Scalpel has Dark/Light/HC + accents. |

#### 3. Code optimisations, performance, refactors, architecture, test and release infrastructure

| # | Change | Key upstream files / commits | Scalpel verdict |
|---|---|---|---|
| O1 | **Form-field design-mode toggle no longer re-renders pages** - `RefreshFormDesignMode` just swaps cursors on existing overlays (tagged `FormOverlayTag`, original cursor kept in an attached DP) instead of `RenderAllAnnotations` per page. | `Controls/Viewer/PdfViewer.Forms.cs` - 2d529190 | **Not applicable** - Scalpel has no form design mode. |
| O2 | **`SaveTempAndReload(preserveRenderedPages: true)`** - skips cancelling render workers and clearing the bitmap cache when a change cannot alter page pixels (field add / fill-colour change), removing the visible flash. | `Shell/TempReload.cs` - 2d529190 | **Not applicable** as-is (Scalpel's form fill is an overlay, no reload). The general pattern (do not drop the render cache for reloads that do not change pixels) is worth remembering for `MainWindow.TempReload.cs`, but no current Scalpel caller needs it. |
| O3 | **Drag handling only commits on real movement** (`_formDragMoved`, 0.5 px threshold) - a click no longer rewrites the file. | `Controls/Viewer/PdfViewer.Forms.cs` - d70a4278 | **Not applicable** - no field move in Scalpel. |
| O4 | **Bitonal raster path** - BGRA -> gray -> 1 bpp packed Flate without alpha mask (`RasterPage.Bitonal`, `PdfImage.FromBitonal`). | `Services/PdfEngineIntegration.cs`, `engine/.../PdfImage.cs` - d70a4278, a6552779 | Covered by **B7 (Applies - port)**. |
| O5 | **Bookmark-failure retry pattern** (`IsBookmarkGraphFailure` walks inner exceptions for "bookmark", rebuild with `preserveBookmarks: false`). | `Services/PdfEngineIntegration.cs` - d70a4278 | Covered by **B9** (Not applicable today; fold into B8). |
| O6 | **Comparison zoom sync compares true zoom levels** rather than view-mode zoom (fixes feedback loops). | `Shell/PdfComparison.cs` - d70a4278, d091fff6 (IDE0031 cleanup) | **Already triaged (see 1.8.1 analysis addendum)** with #301. |
| O7 | **Localisation parity test extended** for the new keys. | `KillerPDF.Tests/LocalizationParityTests.cs` - d70a4278 | **Already in Scalpel** - `Scalpel.Tests/LocaleParityTests.cs` enforces every key in every locale. |
| O8 | **New unit tests**: `OutputPixelDimensionsTests`, `BitonalPageDetectorTests`, `AppDataPathsTests`, `ShortcutTableTests` additions, `TextRunServiceTests` caret test, `PdfBurnRotationTests` cover/image-flip asserts, `PdfEngineIntegrationTests` bitonal/compact/bookmark sanitiser. | `KillerPDF.Tests/*` - d70a4278, 78597c9a, a6552779, 21964e51, d53c3e9d | **Applies - port (S)** only alongside the features they guard (F3 -> pixel-dimension test, B7 -> bitonal detector test, B8 -> bookmark sanitiser test, F4 -> locale mapper test). Scalpel tests link source files directly, so add `<Compile Include>` entries in `Scalpel.Tests/Scalpel.Tests.csproj`. |
| O9 | **Versioned PDF corpus as a release gate (ADR-004)**: provenance-checked public + local-only + fuzz lanes, batch open/save runner, 5 measured passes, per-file status comparison against the previous baseline (1.8.3: 47,024 inputs, zero OK->fail regressions, zero crashes/timeouts). | `engine/docs/architecture/ADR-004-versioned-pdf-corpus.md`, `validation/benchmarks/1.8.3/*` - d70a4278, 1bdd0ff6 | **Applies - port (M, optional).** Scalpel has only 7 hand-made `docs/samples/*.pdf` and the E2E harness. A headless xUnit-or-console runner that pushes a local folder of real-world PDFs through Scalpel's open fallback chain (`PdfReopen`, Modify -> ReadOnly -> Import -> PDFium rebuild) and `PdfSaveGuard.Save`, recording a per-file CSV to diff between versions, would catch exactly the regressions CLAUDE.md warns about. Keep the corpus outside the repo. |
| O10 | **Release preflight: Git sync gate** - refuses to build unless on the default branch, tree clean, and `HEAD == origin/<default>` after fetch. | `release.ps1` - 7f47afeb | **Applies - port (S).** Verified: Scalpel `release.ps1` has no git checks (grep for `git `/`rev-parse`/`porcelain` finds nothing); `docs/RELEASING.md` only suggests `git status --short` manually. Port the ~20 lines; the pre-push auto-bump means "local == origin" must be checked after the second push. |
| O11 | **Release preflight: unit tests (desktop + engine) must pass**; release-date/CHANGELOG checks moved before any build step. | `release.ps1` - d70a4278, f7ac003f | **Split.** Test gate: **Applies - port (S)** - Scalpel `release.ps1` runs no `dotnet test`; add one (note the 8 known pre-existing failures per memory, so either fix them first or gate on a filter). Release-date check: **Not applicable** - Scalpel's `release.ps1` writes `ReleaseDate` from `UtcNow` into `BuildInfo.cs` (L115/L138), so it cannot drift. |
| O12 | Committed startup smoke logs (`tmp/*.log`), brochure PDF rebuilds, version bumps, README source links. | d70a4278, 98c08cd7, a42646b1, d6d515de, 2c91498c, ffb8b3c4 | **Not applicable** (upstream housekeeping). |

#### 4. Engine (KillerPdf.Engine) changes - brief

All engine code is net10-only and not portable; behaviours are mapped above.

| Engine change | Commit | Mapped to |
|---|---|---|
| Hybrid xref: companion stream `/Size` may be <= trailer `/Size`; hybrid entries excluded from the generation-sequence rule | 24774de7 (#314) | B1 - already triaged |
| `PdfImage.FromBitonal` (1 bpp DeviceGray Flate) | d70a4278 | B7 / O4 |
| `PdfPageRasterInformation.ReadBitonalImagePageHints` (per-page "only bitonal images" hint) | a6552779 | B7 |
| `PdfIncrementalPageEditor.ClearBookmarks` use in rebuild/replace paths | d70a4278 | B8 / B9 |
| Tagged-PDF struct tree: wrap multiple top-level `/K` elements when adding a field | 350f4dd6 | B17 - already triaged |
| `PdfFormWidgetReader` exposes `BorderColor` (`/MK /BC`) | 2d529190 | F11 |
| `PdfSignatureReader` / `PdfDetachedSignatureWriter`: empty `/V` string = unsigned | 03f9d8da | B10 - not applicable |
| Engine CHANGELOG / csproj version bumps, engine docs pages (form reading, page import, image insertion) in 14 languages | d70a4278, e2e9e9e2 | Not applicable |

#### 5. Localisation, website, packaging, CI - brief

| Item | Commits | Verdict |
|---|---|---|
| German translation (#313) | 31c1ff78, ca23866a | Already triaged - Scalpel has no German locale |
| Russian translation (#308, #318) | 6bb53718, f518636b, d8189688 | Not applicable - different string sets; Scalpel's `Strings/ru.xaml` is maintained separately |
| French (#304), Italian (#319) | 51970f9e, 3368e07c | Not applicable - Scalpel ships no fr/it |
| Spanish (#328) | 95e6aac8 | Not applicable - wording fixes to upstream keys; Scalpel's `Strings/es.xaml` has its own keys |
| New keys for #227 / #310 / field messages across 15 locales | ada699cf, d70a4278, 78597c9a | See B13 / F3 |
| Website: corpus pages, corpus results, developer guide, homepage blocks, nav/scrollbar fixes, theme dots, landing hierarchy, release info, engine docs i18n | 6502bcf7, c1ca6197, 4450417a, 12a2579c, 31308f37, ee86c9d3, 58ed4976, 4dc2deb1, c9e47f51, 04b0b4d8, 9ed44ea4, 69a3370c, e2340efe, 87f8828a, 6cfb4b42, c7a723db, d03421bd, e2e9e9e2 | Not applicable (upstream site) |
| Chocolatey `.NET 10 desktop runtime` dependency | 2b5a9567 | Not applicable (net10 runtime; Scalpel is net48, Store/Inno) |
| Brochure `KillerPDF.pdf` refreshes | d70a4278, 98c08cd7, a42646b1 | Not applicable |
| CHANGELOG-only commits | 251fff53, 94d2aa5e, 9a6212c2 | Documentation of items above |

---

#### Verdict counts

Counted per row in sections 1-3 (split rows counted once per part; section 4 and 5 rows are
mapping/brief tables and counted separately below).

| Verdict | Sections 1-3 |
|---|---|
| Applies - port | 11 (F1, F3, F4, F11, B7, B8b, B13, O8, O9, O10, O11a) |
| Already in Scalpel | 3 (F2, B8a, O7) |
| Already triaged (see 1.8.1 analysis addendum) | 14 (F5, F6, F7, F10, B1, B2, B3, B4, B5, B6, B12, B16, B17, O6) |
| Not applicable | 16 (F8, F9, B9, B10, B11, B15, B18, B19, B20, O1, O2, O3, O11b, O12, plus O4/O5 covered elsewhere) |
| Needs verification | 1 (B14) |

Section 5: 1 already triaged (German), everything else not applicable. Section 4: all engine
code not applicable; behaviours mapped.

#### Top 5 worth porting

1. **B7 - Save Flattened as 1-bit / 8-bit gray** (M). Real file-size win for scanned documents;
   Scalpel stores every flattened page as 24-bit RGB today.
2. **B13 - Localise the ~100 hard-coded English status/dialog strings** (M). Hebrew/Arabic users
   currently get English status lines; parity test already exists to hold the line.
3. **B8b - Transform keeps document info and bookmarks** (M). The rasterised Transform path
   silently drops title/author and every bookmark.
4. **F4 - First launch follows the Windows display language** (S). Directly relevant for Hebrew
   users; trivial pure mapper plus a test.
5. **F1 - Page-size readout in the status bar** (S) together with **F3 - pixel dimensions in
   Export Images** (S). Small, self-contained, pure helpers ported verbatim.

Honourable mentions: O10/O11a release-script git-sync and test gates (S each), O9 local corpus
open/save runner (M).

---

### Appendix B: Upstream KillerPDF v1.8.3..v1.8.4 - triage for Scalpel

Range: `v1.8.3..v1.8.4`, 50 commits, 105 files (+2092 / -384). Upstream release date 2026-09-06.
Scalpel tree checked: `C:/Code/Personal/ScalpelPDF` (main, working tree as of 2026-09-22).

Upstream's own summary: "1.8.4 addresses document closing, text editing, saving, footer behavior, and
remaining diagnostic translations." Around half the fixes land in parts of upstream that Scalpel does
not have: the dual-pane viewer, the split-pane Compare, the app-size (LayoutTransform) scaling, the
WindowChrome frame, and the engine's tagged-PDF guards. The items that do carry over are page-op
annotation loss, flatten fidelity (JPEG and rotation), the resize scroll drift, the monitor sizing
clamp, and a Save Flattened dirty-flag bug.

Verdict legend: `Applies - port` (with effort S/M/L and a net48 approach) / `Already in Scalpel` /
`Not applicable (reason)` / `Needs verification`.

---

#### 1. New features (user-visible)

| # | Upstream change | Key commits / files | Scalpel verdict |
|---|---|---|---|
| F1 | **#364 Footer page-size readout is clickable and cycles units.** The 1.8.3 footer label (paper name + size) becomes a button. Each click cycles Pixels (at 150 DPI) -> Inches -> Millimeters -> Points, and the choice is saved in the `FooterPageSizeUnit` setting. The tooltip shows all four at once. `PageSizeFormatter.Format` now returns a 7-tuple (label, details, metric, px, in, mm, pt). This went through three designs on the way (metric toggle -> expanded details -> 4-way cycle). | `378d7d1a`, `c471de48`, `9dbea80f`, `88b1970f`; `Services/PageSizeFormatter.cs`, `Features/Viewer/MainWindowViewerHost.cs`, `MainWindow.xaml` (`PageSizeLabel` TextBlock -> Button) | **Not applicable** (Scalpel has no page-size readout: grep for `PageSizeFormatter`/`PageSizeLabel` finds nothing, and the 1.8.3 #301 footer it builds on was never ported). As a standalone feature it would be S-M: a status-bar button computing from `RotationOf(i)` + `Pages[i].Width/Height`. |
| F2 | **#365 Output scale in DPI previews.** The Export Images and Transform DPI readouts now append `xN.NN`, the output-vs-150-DPI scale (`OutputPixelDimensions.ScaleLabel`, a geometric mean of the width and height ratios). The same scale was added to Transform's size readout and then removed as a duplicate. | `0fdf4794`, `c471de48`, `70604597`; `Controls/ExportImagesDialog.cs`, `Controls/TransformWindow.cs`, `Services/OutputPixelDimensions.cs` | **Not applicable** (Scalpel's export (`MainWindow.ExportImages.cs:40`) is a fixed 72/150/300/600 DPI combo with no live pixel readout. 1.8.3's #310 pixel readout was not ported, so there is nothing to append a scale to). |

---

#### 2. Bug fixes

##### 2a. Document tabs and page display (special attention)

| # | Upstream fix | Key commits / files | Scalpel verdict |
|---|---|---|---|
| B1 | **#353 Stale or repeated tab-close requests crashed.** `CloseTab` is now split into a guard plus `CloseTabCore`. A `_closingTab` re-entrancy flag rejects a second close while one is running. The session must still be in `_sessions`, and this is re-checked after `FocusViewer` and after the dirty prompt. After the prompt the code also requires `ReferenceEquals(_active, s)` before closing. `MainWindowTabStubs.CloseTab` now routes the close to whichever pane (Viewer/ViewerB) owns the session, not always the active pane. | `386d0de0`; `Controls/Viewer/PdfViewer.Tabs.cs`, `Features/Viewer/MainWindowTabStubs.cs` | **Already in Scalpel.** `MainWindow.Tabs.cs` tracks tabs as a list of **paths**, not session objects, so a close can never refer to a stale session. `CloseTab` finds the tab with `FindIndex(...)` and returns early if `idx < 0` (line 136-137), removes tabs by path with `RemoveAll`, and wraps everything in `try/catch`. `OpenFile` is synchronous and `ScalpelDialog` is modal, so a second close cannot run while one is in progress. There is one pane, so no routing is needed. Two nearby Scalpel-only gaps are listed as S2/S3 below. |
| B2 | **#378 / #379 Wrong first page after opening or switching documents in Single, Two-Page and Grid.** This was a 1.8.3 regression: `RefreshPageList` re-seated the sidebar under `_syncingPageList`, so `PageList_SelectionChanged` returned before calling `RenderPage`. The previous tab's page 1 stayed in the primary tile, which was most visible in Grid. The fix: `BootstrapDocumentView` now calls `RenderPage(Grid ? 0 : page)` itself for the non-continuous modes. Community PR by @Ryokoxx. | `3cd263ab`, merge `7d083edb`; `Controls/Viewer/PdfViewer.Viewport.cs` | **Not applicable (the cause is not in Scalpel).** There is no `_syncingPageList` guard. `FinishOpenFile` (`MainWindow.FileOps.cs:618`) replaces `ItemsSource` (which resets the selection to -1) and then sets `PageList.SelectedIndex = 0`, so `PageList_SelectionChanged` always fires. It calls `RenderPage(SelectedIndex)` for Single/Two-Page, and `RenderPage(0)` for Grid once `ClearSecondaryPages` has cut the panel to one child (`MainWindow.PageSelection.cs:95-99`). A related but separate Scalpel issue in the sidebar is listed as S1. |
| B3 | **#354 Footer page number went stale during Continuous navigation.** After explicit navigation (the page was already assigned before the scroll settled), the early-return branch now also refreshes the `Str_PageOf` status. | `8b10abcc`; `PdfViewer.Viewport.cs` | **Already in Scalpel.** In Continuous, both paths update the page box: explicit navigation through `PageList_SelectionChanged` (`MainWindow.PageSelection.cs:85`) and scrolling through `PagePreviewPanel_ScrollChanged` (`MainWindow.ViewMode.cs:107-113`). |

##### 2b. Saving, flattening, editing

| # | Upstream fix | Key commits / files | Scalpel verdict |
|---|---|---|---|
| B4 | **#388 Pasted images and other unsaved annotations were lost when inserting blank pages.** New `PageAnnotationInsertion.Shift(_annotations, insertIndex, 1)` moves every annotation on a page at or after the insertion index up by one page (updating both the dictionary key and `PageIndex`). `SaveTempAndReload` is then called with `keepAnnotations: true` and a captured document undo. If anything fails, a snapshot is restored. Insert-at-end also keeps annotations now. | `b96295aa`; `Shell/PageOperations.cs`, `KillerPDF.Tests/PageAnnotationInsertionTests.cs` | **Applies - port (M).** Scalpel is worse off than upstream was. `InsertBlankPage_Click` (`MainWindow.FileToolbar.cs:346-356`), `MoveUp_Click`/`MoveDown_Click` (lines 365-386), delete pages (line 337), merge (line 104), drag reorder (`MainWindow.DragDrop.cs:92`) and `MainWindow.Links.cs:186` all call `SaveTempAndReload()` with the default `keepAnnotations:false`, which runs `_annotations.Clear()` (`MainWindow.TempReload.cs:78`). Every unsaved annotation, of any type, is silently dropped. **Approach:** add a WPF-free `Services/AnnotationPageRemap.cs` with `Insert(at, count)`, `Remove(indices)`, `Move(from, to)` and `Append` (no-op), which rekeys the `Dictionary<int, List<PageAnnotation>>` and sets `PageIndex`. Call it before each `SaveTempAndReload(keepAnnotations: true)`, restore from a snapshot on exception, and link it into `Scalpel.Tests` with unit tests. |
| B5 | **#356 (a) Detected text size preserved.** Upstream removed its `EditTextSizeCorrection = 0.8` shrink factor and stopped rounding re-edit and cover-edit sizes to whole points. The size box now shows `0.##` instead of `F0`, and uses `Math.Clamp(v,1,400)` without rounding. | `8bad3190`; `PdfViewer.TextEditing.cs`, `Shell/TextSettingsBar.cs`, `MainWindow.xaml.cs` | **Applies - port (S), mostly already done.** Scalpel never had the 0.8 factor: `MainWindow.InlineTextEdit.cs:238` uses PdfPig `PointSize * syInv`. Remaining differences: (1) re-editing a placed text box still rounds, `Math.Round(placed.FontSize * syp)` at `MainWindow.InlineTextEdit.cs:121`, so a 10.5 pt box comes back as 10 or 11; (2) the TextBar size combo formats with `"0"` (`MainWindow.TextBar.cs:71`), so fractional sizes display rounded; (3) the detected-edit box clamps to at least 10 canvas units (`InlineTextEdit.cs:287/296/306`), which inflates very small source text. Fix: drop the `Math.Round`, format with `"0.##"`, and lower the clamp to about `syInv` (1 pt). |
| B6 | **#356 (b) Report unavailable font substitution.** New `PdfFontStyle.ResolveInstalledFamily` returns the installed spelling, or "Segoe UI". The status bar shows `Str_St_TextEditDetectedFont` or `Str_St_TextEditFontSubstituted` ("Font X is unavailable. Editing with Y, N pt."). | `8bad3190`; `Services/PdfFontStyle.cs`, 15 locale files | **Already in Scalpel.** It goes further: `MainWindow.InlineTextEdit.cs:224-312` resolves the family through `FontResolver`, keeps the document's embedded font bytes when the font is not installed (`embeddedBytes`, `ExactFontFamily`), and carries `FontDisplay` into the edit. |
| B7 | **#356 (c) Save Flattened crashed on edited tagged PDFs.** The burn step for rasterization now appends overlays as `/Artifact BMC` marked content (`PdfEngineBurn.Burn(..., forRasterization: true)` -> `editor.AppendPageArtifact`), so the engine's tagged-structure guard no longer throws. | `8bad3190`; `Services/PdfEngineBurn.cs`, engine `PdfIncrementalPageEditor.cs` | **Not applicable (engine-specific).** PdfSharpCore enforces no tagged-structure rules, and Scalpel's flatten draws annotations straight onto `XGraphics` (`DrawAnnotationsOnDocument`). |
| B8 | **#356 (d) Save Flattened restructured.** The whole body now sits inside one `try` with an `operationStarted` flag, so a failure before the async step no longer leaks the overlay or the cancel op. Upstream also **removed `MarkDirty(false)`** after a flattened export and stopped swapping `_doc` to the clean temp copy, because exporting a flattened copy to another path does not save the working document. | `8bad3190`; `Shell/FileOperations.cs` | **Applies - port (S). This is a data-loss bug in Scalpel.** `SaveFlattened_Click` (`MainWindow.FileToolbar.cs:580-700`) calls `MarkDirty(false)` after writing the flattened file to a **different** path. The in-memory `_annotations` and form edits stay unsaved, yet closing the tab or app no longer prompts (`MainWindow.WindowChrome.cs` `OnClosing` checks `_isDirty`). Fix: delete that `MarkDirty(false)`. Optionally move the pre-dialog work (`CommitActiveTextBox`, `WriteFormValuesToDocument`) and the temp burn into the `try`, so a PdfSharpCore save failure there shows the error dialog instead of reaching the `async void` crash sink. |
| B9 | **#362 Landscape orientation lost when flattening rotated PDFs.** The flatten page-size snapshot now uses `engineSession.VisualPageSize(i, _pageRotations)` (CropBox- and rotation-aware) instead of the raw width/height. New `VisualPageSize` tests cover native `/Rotate` and app-side rotations. | `beb3a509`; `Shell/FileOperations.cs`, `KillerPDF.Tests/PdfEngineDocumentSessionTests.cs` | **Needs verification.** Scalpel's flatten (`MainWindow.FileToolbar.cs:621-627`) uses `p.Width.Point, p.Height.Point` and renders with `PageDimensions(dimMin, dimMax)`. `SaveTempAndReload` restores `/Rotate` into the in-memory doc (`MainWindow.TempReload.cs:141-143`), so page size and render both depend on how PdfSharpCore's `PdfPage.Width` (orientation-swapped on `/Rotate 90`) and Docnet's MediaBox-based bitmap sizing interact. The comment at `TempReload.cs:86-90` says Docnet clips rotated content. Test: flatten a native `/Rotate 90` page and a page rotated with Scalpel's Rotate button, then compare output aspect ratio and content. If it is wrong: (M) snapshot dims via `RotationOf(i)` with a swap for 90/270, render from a zero-rotation temp copy, apply `RotateBitmapStatic`, and add an xUnit test. |
| B10 | **#366 JPEG compression lost when flattening, repairing, and importing image-based pages.** Pages whose image XObjects are all DCT (`DCTDecode`/`DCT`, found by walking nested Form XObjects with a visited set) are re-encoded as JPEG (`BitmapHelpers.EncodeJpeg`, 150 DPI) instead of lossless. The same hints are applied in the raster-repair path (`PdfImport`) and the CLI rasterize. Image-to-PDF import of a single-frame `.jpg` embeds the original bytes. Bitonal pages keep their 1-bit path. | `3f3973bc`, `23a80b43`; `Services/PdfRasterize.cs`, `Services/PdfImport.cs`, `Services/PdfEngineIntegration.cs`, `Features/Cli/CliRunner.cs`, engine `PdfPageRasterInformation.cs` | **Applies - port (M).** Scalpel always encodes PNG: flatten `RenderToPng` (`MainWindow.FileToolbar.cs:669/766`) and PDFium repair-rasterize (`MainWindow.FileOps.cs:575`). Flattening a scanned (JPEG) document can therefore grow it several times over. **Approach:** add `Services/PageImageKind.cs`, which uses PdfSharpCore (`page.Resources.Elements["/XObject"]`, recursing into `/Subtype /Form` resources with a visited-reference set) to classify a page as "all images DCT". For those pages encode with WPF `JpegBitmapEncoder { QualityLevel = 85-90 }`. `XImage.FromStream` on JPEG bytes is passed through as DCTDecode by PdfSharpCore. Already fine and needs no change: placed images (`ImageAnnotation`) keep their original file bytes (`MainWindow.Signatures.cs:621-642`), and Scalpel has no CLI or image-to-PDF import. |
| B11 | **#384 Document Info could not be saved when the PDF carried a malformed `/Lang`.** `ApplyDocumentMetadata` catches the engine's `ArgumentException` and retries with `Language = null`. | `9ff06772`; `Services/PdfEngineIntegration.cs` | **Not applicable.** Scalpel's Document Info (`MainWindow.Tools.cs:242-263`) edits only Title/Author/Subject/Keywords/Creator through PdfSharpCore `_doc.Info`, which does not validate `/Lang`. |
| B12 | **#383 Transform could not rasterize selected pages in tagged PDFs.** `ReplacePagesAndCompact` now passes `allowUntaggedImports: true` (new `PdfIncrementalPageEditor.AllowUntaggedPageImports()`). | `fcafefb5`; `Services/PdfEngineIntegration.cs`, engine | **Not applicable (engine tagged-structure guard).** Scalpel's `Services/TransformService.cs` uses PdfSharpCore, which has no such guard. |
| B13 | **#389 Pasted clipboard images came out invisible.** Some clipboard sources supply a Bgra32 frame whose alpha is all zeros. New `BitmapHelpers.EnsureVisibleClipboardAlpha` forces alpha to 255 only when **no** pixel has non-zero alpha, so genuine transparency is kept. | `29d97dd7`; `Services/BitmapHelpers.cs`, `Shell/TextSettingsBar.cs`, `ClipboardImageTests.cs` | **Not applicable** (Scalpel has no clipboard image paste: grep for `Clipboard.GetImage`/`ContainsImage` finds nothing). If paste is ever added, reuse this rule. |

##### 2c. Window chrome and sizing

| # | Upstream fix | Key commits / files | Scalpel verdict |
|---|---|---|---|
| B14 | **#363 Window could not maximize properly after moving to a larger monitor.** `WM_GETMINMAXINFO` set `ptMaxTrackSize` to the *source* monitor's work area, which clamped a drag-maximize onto a bigger monitor. It now keeps Windows' desktop-wide limit: `Math.Max(mmi.ptMaxTrackSize, mmi.ptMaxSize)`. | `11558725`; `Shell/WindowChrome.cs` | **Applies - port (S).** Scalpel has the identical code at `MainWindow.Settings.cs:307-308` (`mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x; ...y = ...y`). This matters on this PC's dual 2560x1440 setup whenever monitor DPI or work-area size differs. Two-line change. |
| B15 | **#373 Continuous view lost its place during window resize.** Fit-to-width/page used to call `NavigateContinuousToPage(currentPage)` on every resize tick, which made the document and thumbnails jump. New `FitScroll { PageTop, KeepOffset }`: a resize re-fit calls `CarryContinuousOffset(previousZoom)`, which scales `VerticalOffset` by `newZoom/oldZoom`, while user fits still snap to the page top. Community PR. | `53b50875`; `PdfViewer.Viewport.cs` | **Applies - port (S).** Scalpel has the opposite version of the same problem. `PagePreviewPanel_SizeChanged` -> `FitToWidth()`/`FitToPage()` (`MainWindow.Zoom.cs:350-368, 453-462`) changes the `LayoutTransform` scale in Continuous without touching `VerticalOffset`, so the content under the viewport drifts in proportion to the zoom change on every resize. Fix: in both continuous branches, record `prev = _zoomLevel` before the change and call `PagePreviewPanel.ScrollToVerticalOffset(offset * _zoomLevel / prev)` after `ApplyZoom()`. |
| B16 | **#372 Native resize borders plus custom caption.** WPF `WindowChrome` is dropped for `WindowStyle="SingleBorderWindow"` plus `WM_NCCALCSIZE`, which strips only the caption so Windows owns the left/right/bottom borders. Upstream owning every edge made top/left drags jitter. `WM_NCHITTEST` synthesizes the top grip and the caption. Community PR. | `805cffbc`; `MainWindow.xaml`, `Shell/WindowChrome.cs`, `Shell/AppScale.cs` | **Not applicable (different frame design).** Scalpel uses `WindowStyle="None" AllowsTransparency="True"` (`MainWindow.xaml:10-11`) with its own `WmNcHitTest` (`MainWindow.Settings.cs:245`). Side note: upstream moved off `AllowsTransparency` earlier because it forces software rendering. Moving Scalpel to this NCCALCSIZE design could help drag and animation smoothness on the 180 Hz displays, but that is a separate project (L) and **needs verification** before anyone commits to it. |
| B17 | **#380 Title-bar drag and double-click from maximized became unreliable** (a regression from B16). The custom title bar is now classified before `DefWindowProc`, so the stale invisible native caption zones cannot override it. | `49f3d996`; `Shell/WindowChrome.cs` | **Already in Scalpel.** `TitleBar_MouseLeftButtonDown` (`MainWindow.WindowChrome.cs:29-43`) sends `WM_NCLBUTTONDOWN(HTCAPTION)`, which gives native restore-and-drag from maximized, and handles double-click with `MaximizeBtn_Click`. |
| B18 | **Title-bar controls restored (close button, logo zoom).** Another follow-up regression from B16: caption controls must return `HTCLIENT` before the top resize grip is considered, and they are hit-tested through `TitleBarBorder.InputHitTest`. | `1571a6ef`; `Shell/WindowChrome.cs` | **Not applicable** (upstream fixing its own regression from B16). |
| B19 | **#355 Footer controls did not scale with the app-size setting.** `FooterDocumentControls` now gets the same `LayoutTransform`, and the footer badge-centering math multiplies by `_appScale`. | `13093b07`; `Shell/AppScale.cs`, `Shell/SidebarLayout.cs` | **Not applicable** (Scalpel has no app-wide UI scale: no `AppScale`/`LayoutTransform` on the chrome). |

##### 2d. Compare, toolbar captions, themes, installer, localization

| # | Upstream fix | Key commits / files | Scalpel verdict |
|---|---|---|---|
| B20 | **#370 Comparison kept display, save and working-copy paths separate.** New `ComparisonDocument(WorkingPath, OriginalPath, Title)` record. Choices are de-duplicated by the original path, and the label uses the original file name instead of the temp copy's. | `56ebda9e`, `756633de`; `PdfViewer.TabsApi.cs`, `Shell/PdfComparison.cs` | **Not applicable.** Scalpel's Compare is a text report (`MainWindow.Compare.cs`) and already labels with `_originalFile` (line 98). |
| B21 | **#371 Comparison chip contrast** (`SelectionFg` -> `OnPrimaryBrush`). | `ffaf83bf`; `MainWindow.xaml` | **Not applicable** (no comparison chip in Scalpel). |
| B22 | **#376 Underscores disappeared from file names in the comparison menu.** A string `MenuItem.Header` treats `_` as an access-key marker, so the fix doubles it. | `0271c88c`; `Shell/PdfComparison.cs` | **Already in Scalpel (by template).** The only `MenuItem` template (`Themes/_Shared.xaml:549-565`) uses `<ContentPresenter ContentSource="Header">` with the default `RecognizesAccessKey=False`, so underscores in the Recent-files menu (`MainWindow.Recent.cs:84`) render literally. If that template ever sets `RecognizesAccessKey="True"`, apply the `Replace("_","__")` fix. |
| B23 | **#360 Compare toolbar caption and overflow-menu choices.** A custom two-document glyph and caption for `ComparePdfBtn`. The overflow click is marked handled so the choices menu stays open, and the menu uses `MakeThemedMenu()`. | `c1d152f3`; `Shell/PdfComparison.cs`, `Shell/SettingsPanel.cs` | **Not applicable** (Scalpel's ribbon has no overflow menu or glyph-to-caption map). |
| B24 | **#359 Comparison opened with unequal pane widths.** `BalanceComparisonPanes()` splits `(SplitHost.ActualWidth - gutter)/2`, excluding the sidebar. | `863ac964`; `Shell/PdfComparison.cs`, `Shell/SplitPane.cs` | **Not applicable** (no split pane). |
| B25 | **Form-field and measure toolbar captions restored** (a custom "T in a box" glyph and the `\uED5E` -> `Str_Lbl_Measure` mapping). | `5aa6ce0c`; `Shell/SettingsPanel.cs` | **Not applicable** (Scalpel's ribbon buttons carry their labels in XAML). |
| B26 | **Malaise theme selection colours corrected.** | `2b2c88a7`; `Themes/Malaise.xaml` | **Not applicable** (upstream-only theme). |
| B27 | **PDF handler registered under the product name** (`Applications\KillerPDF.App.exe` with `FriendlyAppName`, `shell\open\command`, `SupportedTypes\.pdf`; removed on uninstall). Before this, "Open with" showed the internal EXE name. | `c81e5696`; `App.xaml.cs` | **Already in Scalpel.** `App.xaml.cs:1274-1286` writes `Software\Classes\Applications\Scalpel.exe` with `FriendlyAppName="Scalpel"`, `DefaultIcon`, `shell\open\command` and `SupportedTypes`. `Services/Installer.cs:44` lists it for cleanup. |
| B28 | **#361 Installer details and installed size in Windows' program list.** The Uninstall key gains `EstimatedSize` (a recursive KB sum of the install dir that skips reparse points). The portable/installer launcher gets `AssemblyTitle` ("KillerPDF Installer") and `Copyright` file-version metadata. | `4ef6d390`; `App.xaml.cs`, `build/build-portable.ps1` | **Applies - port (S).** Scalpel's self-install Uninstall key (`App.xaml.cs:1229-1245`) has no `EstimatedSize`, so Settings > Apps shows no size. `Scalpel.csproj` sets no `Copyright`/`Company`/`Product`/`Description`, so Explorer > Properties > Details is sparse. **Approach:** add `key.SetValue("EstimatedSize", kb, RegistryValueKind.DWord)`, computed with `Directory.EnumerateFiles(InstallDir, "*", SearchOption.AllDirectories)` in a try/catch (net48 has no `EnumerationOptions`). Add `<Company>`, `<Copyright>`, `<Product>` and `<Description>` to `Scalpel.csproj`. Inno already computes size for the installer channel, and MSIX does not use this key. |
| B29 | **#227 Round-trip validation failures localized.** Engine `PdfRoundTripFailure` record + `PdfRoundTripFailureCode` enum + `PdfRoundTripMessages.resx`, with messages in all 15 languages and numeric details formatted per culture. | `be454f7f`; engine `Validation/*` | **Not applicable** (engine validator; Scalpel has no round-trip validator). |
| B30 | **#227 Audit of hardcoded interface text plus a CI gate.** Twelve English literals moved to `Str_*` keys: annotation-kind labels in the context menu, print preview "Preparing to print", "Could not render preview", duplex tooltip, "Go to page {0}" link tooltip, "Measurement unavailable", repair failure, signature-field prompts, portable tooltip. A new `LocalizationParityTests.UiPropertiesDoNotIntroduceHardcodedEnglish` regex-scans all `.xaml` and `.cs` for `Text=/Content=/Header=/ToolTip=/Title=` string literals containing letters and fails on anything not on an allowlist. | `0c8a6587`; `Shell/ContextMenu.cs`, `Controls/PrintPreviewWindow.cs`, `PdfViewer.Links/Forms/Measurement.cs`, `LocalizationParityTests.cs`, 15 locale files | **Applies - port (M for the gate plus the obvious strings, L for full cleanup).** Scalpel ships 9 locales but still has a lot of hardcoded English: about 19 `ScalpelDialog.Show(this, "...")` calls, about 50 `SetStatus("...")`/`SetStatus($"...")` calls, plus menu headers and titles such as `"Insert Blank Page After"` (`MainWindow.ContextMenu.cs:82`), `"Copy URL"` (`MainWindow.Links.cs:145`), `"Insert Image"` (`MainWindow.Signatures.cs:612`), `"Save Flattened PDF"` and `"Flattened PDF saved to..."` (`MainWindow.FileToolbar.cs:587/693`). `Scalpel.Tests/LocaleParityTests.cs` only checks key parity, placeholders and duplicates. **Approach:** copy the regex gate, adding `ScalpelDialog.Show(`/`SetStatus(` string-literal patterns and a Scalpel allowlist. Start it with a baseline "known offenders" list so it goes green immediately, then shrink that list. |

##### 2e. Scalpel-side findings surfaced while checking the tab and first-page fixes

These are not upstream items. They are real gaps in the Scalpel code, found while checking B1/B2.

| # | Finding | Scalpel file | Verdict |
|---|---|---|---|
| S1 | **The sidebar shows the previous document's thumbnails after a tab switch.** `RefreshPageList` seeds each new `PageThumbnailVm` with `oldItems[i].Thumbnail` "so the list never flashes blank". That is meant for a same-document reload (rotation) but also runs when a **different** file opens. The previous tab's page images stay in the sidebar until the background loader replaces them, and indefinitely for any page whose thumbnail fails to render. This is Scalpel's own version of #378. | `MainWindow.FileOps.cs:826-841` | **Applies - port (S).** Record which document the thumbnails came from (for example `_thumbSourceDoc = _originalFile`) and only carry them forward when it matches. |
| S2 | **Cancelling the unsaved-changes prompt on the last tab loses the tab entry.** `CloseTab` removes the tab (`_openTabs.RemoveAt(idx)`) **before** calling `CloseFile()`. If the user answers No to the dirty prompt, the document stays open but is no longer in `_openTabs`, so the next file opened appears as the only tab. | `MainWindow.Tabs.cs:157-162`, `MainWindow.DirtyTracking.cs:55-61` | **Applies - port (S).** Call `CloseFile()` first and remove the entry only if `_doc is null` afterwards. The recursion guard at `DirtyTracking.cs:50` already requires `_openTabs.Count > 1`. |
| S3 | **A failed open during a tab switch can leave the strip pointing at a document that is gone.** `OpenFile` closes the current `_doc` before `PdfReader.Open`. If every fallback fails (`ReportOpenFailure`), `_doc` is null but `_originalFile` and the tab strip still show the previous file as active, and `CloseTab`'s "switch was cancelled" check (`Tabs.cs:152`) then keeps the tab. | `MainWindow.FileOps.cs:69-71`, `MainWindow.Tabs.cs:122,152` | **Needs verification** (repro: open two files, corrupt one on disk, switch to it). A likely fix is to open the new file into a local variable first and swap only on success. |

---

#### 3. Code optimizations, performance, refactors, architecture (including commit-only changes)

| # | Change | Commits / files | Scalpel verdict |
|---|---|---|---|
| R1 | `CloseTab` -> guard + `CloseTabCore` with the `_closingTab` re-entrancy flag, plus pane-aware routing in `MainWindowTabStubs` (see B1). | `386d0de0` | **Already in Scalpel** (path model, see B1). |
| R2 | `BootstrapDocumentView` now renders the primary page itself instead of relying on the zoom re-sharpen timer to do it by accident (see B2). | `3cd263ab` | **Not applicable** (see B2). |
| R3 | `FitScroll` enum + `CarryContinuousOffset`: resize re-fits keep the offset and user fits snap to the page top (see B15). | `53b50875` | **Applies - port (S)** (see B15). |
| R4 | Window frame rewrite: `WindowChrome` dropped, `WM_NCCALCSIZE`/`WM_NCHITTEST` added, the `IsHitTestVisibleInChrome` attributes removed throughout the XAML, and title-bar hit testing moved to `TitleBarBorder.InputHitTest` (B16-B18). | `805cffbc`, `49f3d996`, `1571a6ef` | **Not applicable** (see B16; the GPU-composition side point there needs verification). |
| R5 | Save Flattened restructured into one `try/finally` with an `operationStarted` flag. It no longer reopens `_doc` from the clean temp copy or clears the dirty flag (see B8). | `8bad3190` | **Applies - port (S)** (see B8). |
| R6 | `PageAnnotationInsertion.Shift` as a testable pure helper with snapshot rollback, plus `CaptureDocumentUndo()` passed into `SaveTempAndReload(documentUndo:)` so a blank-page insert is one undo step (see B4). | `b96295aa` | **Applies - port (M)** (see B4; Scalpel's undo stack is cleared on reload, so full parity would add document-level undo, which is out of scope for the S/M fix). |
| R7 | JPEG raster hints: `PdfPageRasterInformation.ReadJpegImagePageHints` recurses into Form XObjects with a visited set, accepts `DCT`/`DCTDecode` names and arrays, and requires at least one image. This is shared by flatten, repair, CLI rasterize and image import (see B10). | `3f3973bc`, `23a80b43` | **Applies - port (M)** (see B10). |
| R8 | `PdfEngineBurn.Burn(forRasterization:)` -> artifact overlays (see B7). | `8bad3190` | **Not applicable** (engine). |
| R9 | `PageSizeFormatter` returns a 7-tuple, `OutputPixelDimensions.ScaleLabel` added, and `FooterPageSizeUnit` setting parsing accepts the legacy `Imperial`/`Metric` values (F1, F2). | `378d7d1a`..`88b1970f` | **Not applicable** (F1/F2). |
| R10 | `BitmapHelpers.EncodeClipboardImagePng`: format-converts to Bgra32, keeps DPI, and applies the zero-alpha rule (B13). | `29d97dd7` | **Not applicable** (no paste). |
| R11 | `GetInstalledSizeKilobytes` with `EnumerationOptions { IgnoreInaccessible, AttributesToSkip = ReparsePoint }` (B28). | `4ef6d390` | **Applies - port (S)** (see B28; on net48 use `Directory.EnumerateFiles` + try/catch). |
| R12 | `PdfFontStyle.ResolveInstalledFamily`: separator-insensitive match of a PDF font family against installed fonts (B6). | `8bad3190` | **Already in Scalpel** (`Services/FontResolver.cs`). |
| R13 | **Test infrastructure:** `ClipboardImageTests` (new), `PageAnnotationInsertionTests` (+shift test), `PdfEngineDocumentSessionTests` (+`VisualPageSize` rotation matrix), `PdfBurnRotationTests` (+tagged raster preparation), `PdfFontStyleTests` (+installed-family resolution), `OutputPixelDimensionsTests` (+scale label), `PdfEngineIntegrationTests` (+JPEG, tagged transform, invalid /Lang), `LocalizationParityTests` (+hardcoded-UI gate, +12 required keys), engine `PdfRoundTripFailureTests`, `PdfPageRasterInformationTests`, `PdfIncrementalPageEditorTests`. `KillerPDF.Tests.csproj` gained one compile link. | various | **Applies - port** only alongside B4 (annotation remap tests), B30 (gate), B9 (rotated-flatten test if it proves wrong) and B10 (DCT-page classifier test). The rest are **Not applicable** (engine or feature absent). Remember the Scalpel test project links sources directly (`Scalpel.Tests.csproj`), so new `Services/*.cs` helpers need `<Compile Include>` links. |
| R14 | **Build/release tooling:** the engine NuGet package is now published automatically from GitHub app releases (`.github/workflows/nuget-engine-release.yml`, `source_ref` retry, workflow artifacts kept). App and engine versions must match, enforced by an MSBuild `VerifyAppEngineVersion` target (`XmlPeek` of `KillerPDF.csproj`) and by `release.ps1`. `docs/engine-publishing.md` added. | `f60a01db`, `c74c7a95` | **Not applicable** (no engine or NuGet package in Scalpel). |
| R15 | **Maintenance forward-port checker:** `.github/maintenance-forward-ports.json` + `build/Check-MaintenanceForwardPorts.ps1` + a self-test, checking that fixes made on the maintenance line also reach the "Overkill" development branch. A GitHub workflow for it was added and then removed as "unrequested", and the checker was fixed to exit 0 on success. `docs/maintenance-forward-ports.md`. | `b244ee3a`, `8c2face7`, `354acd2e` | **Not applicable** (Scalpel has a single `main` line). |
| R16 | Housekeeping: version bump to 1.8.4 and `ReleaseDate` set (`6ad82eb0`, `8102c4c7`); `.gitignore` now ignores all of `/artifacts/` and `/tmp/`, and a committed smoke-log and artifact PDF were removed (`4827666c`); changelog commits (`3fd39538`). | as listed | **Not applicable.** |

---

#### 4. Engine (KillerPdf.Engine) changes, briefly

All **Not applicable** to Scalpel (PdfSharpCore/Docnet/PdfPig stack). Listed for completeness:

- `Validation/PdfRoundTripFailure.cs` (new) + `PdfRoundTripMessages.resx` (new, 15 languages): structured, culture-independent failure codes (`SourceInspection`, `RewrittenInspection`, `SecondAuthenticatedInspection`, `AuthenticatedGraphMismatch`, `RewriteMismatch`, `AuthenticationRequired`, `AuthenticationFailed`) with numeric detail fields. `PdfRoundTripResult.Failure` added. The corpus runner prints the localized summary plus findings. (`be454f7f`, #227)
- `Editing/PdfIncrementalPageEditor.cs`: `AppendPageArtifact` (an overlay wrapped in `/Artifact BMC`, which rejects content that has MCIDs) (#356), and `AllowUntaggedPageImports()` for deliberate raster replacement in tagged docs (#383). (`8bad3190`, `fcafefb5`)
- `Documents/PdfPageRasterInformation.cs`: `ReadJpegImagePageHints` with recursive Form XObject traversal (#366). (`3f3973bc`, `23a80b43`)
- `KillerPdf.Engine.csproj`: version 1.8.2 -> 1.8.4, plus the app/engine version-sync MSBuild target. Engine CHANGELOG adds the 1.8.3 and 1.8.4 sections.

---

#### 5. Localization, website, packaging, CI (brief)

| Area | Change | Scalpel verdict |
|---|---|---|
| Localization | 14 new keys in all 15 upstream locales: `Str_St_TextEditDetectedFont`, `Str_St_TextEditFontSubstituted` (#356) and 12 audit keys (#227: `Str_Print_NoTwoSidedSupport`, `Str_Print_PreviewFailed`, `Str_Print_PreparingToPrint`, `Str_Form_ClickInitials`, `Str_Form_ClickSign`, `Str_Form_Initial`, `Str_Compare_PreviousRegion`, `Str_Compare_NextRegion`, `Str_Portable_InstallTip`, `Str_Link_GoToPage`, `Str_Measurement_Unavailable`, `Str_Repair_Unrecoverable`) plus `Str_Annot_*` kind labels. | **Not applicable** as a key set (different key names and features). The approach applies through B30. |
| Website | `pdf-landing/` release info, i18n strings, 8 refreshed screenshots (`8383acd6`, `d2db0b3f`, `eac3a045`); README source link (`fbf02cd5`); 1.8-series brochure PDF (`4cd50968`). | **Not applicable.** |
| Packaging | Launcher `AssemblyTitle`/`Copyright` in `build/build-portable.ps1`; `EstimatedSize` in the Uninstall key (#361). | **Applies - port (S)**, see B28. |
| CI | NuGet engine release workflow; maintenance forward-port workflow added and removed. | **Not applicable.** |

---

#### Verdict tally

Counted over the 54 rows in sections 1-3 and 5 (F1-F2, B1-B30, S1-S3, R1-R16, and the Localization/Website/CI rows of section 5). The engine bullets in section 4 are all Not applicable and not counted separately. The Packaging row repeats B28 and is not counted again. Many R rows restate a B row from the refactor angle, so the distinct work is smaller than the counts suggest (see below).

| Verdict | Count | Items |
|---|---|---|
| Applies - port | 16 | B4, B5, B8, B10, B14, B15, B28, B30, S1, S2, R3, R5, R6, R7, R11, R13 (tests ported alongside) |
| Already in Scalpel | 8 | B1, B3, B6, B17, B22, B27, R1, R12 |
| Not applicable | 28 | F1, F2, B2, B7, B11, B12, B13, B16, B18, B19, B20, B21, B23, B24, B25, B26, B29, R2, R4, R8, R9, R10, R14, R15, R16, Localization, Website, CI |
| Needs verification | 2 | B9 (rotated flatten), S3 (failed open during tab switch) |

The distinct actionable work reduces to 10 port tasks: B4/R6, B5, B8/R5, B10/R7, B14, B15/R3, B28/R11, B30, S1, S2.

#### Top 5 worth porting (by user impact per effort)

1. **B8 - Save Flattened clears the dirty flag (data loss), S.** Remove `MarkDirty(false)` at `MainWindow.FileToolbar.cs` after the flattened export. Right now, after exporting a flattened copy, the user can close the app with no prompt and lose every unsaved annotation.
2. **B4 - Page operations drop all unsaved annotations, M.** Insert blank, move up/down, delete, merge and drag reorder all call `SaveTempAndReload()` without `keepAnnotations`. Add an annotation page-remap helper and pass `keepAnnotations: true`.
3. **B14 - `ptMaxTrackSize` clamp across monitors, S.** A two-line fix at `MainWindow.Settings.cs:307-308`.
4. **B15 - Continuous view drifts on resize, S.** Scale `VerticalOffset` by the zoom ratio in the continuous branches of `FitToWidth`/`FitToPage` (`MainWindow.Zoom.cs`).
5. **B10 - Keep JPEG when flattening or repairing JPEG-only pages, M.** Classify DCT-only pages with PdfSharpCore and encode with `JpegBitmapEncoder` instead of PNG. This avoids large size blow-ups on scanned documents.

Close runners-up: S1 (stale sidebar thumbnails after a tab switch, S), S2 (last-tab cancel drops the tab entry, S), B30 (hardcoded-English gate, M), B28 (EstimatedSize and file-version details, S), B5 (fractional font sizes on re-edit, S). B9 (rotated flatten) needs a quick repro before anyone commits to it.

---

### Appendix C: Upstream KillerPDF v1.8.4..v1.8.61 - triage for Scalpel

Range: `v1.8.4..v1.8.61`, 77 commits, 112 files, releases **1.8.5** (2026-09-14), **1.8.6** (2026-09-22), **1.8.61** (2026-09-22).
Upstream now runs a **1.8.x maintenance branch** (`main`) plus a **1.9 "Overkill" development branch** (`dev/1.9-overkill`); every maintenance fix in this range is recorded as forward-ported in `.github/maintenance-forward-ports.json`.

Verdict legend: `Applies - port` (with effort S/M/L and approach) / `Already in Scalpel` / `Not applicable (reason)` / `Needs verification`.
Every "Applies" and "Already" verdict below was checked by grepping the Scalpel tree at `C:/Code/Personal/ScalpelPDF`.

---

#### 1. New features (user-visible)

| # | Item | Upstream commits / files | Scalpel verdict |
|---|------|--------------------------|-----------------|
| F1 | **Startup update check**, on by default, Yes/No confirm before self-update, "Always check on startup" checkbox inside the update prompt shared with an About toggle; skips prompt if any tab is dirty or a dialog is open; stable-tag regex rejects drafts/prereleases | `44b776aa`, `eaa94748`, `58772eff`; `Services/ReleaseUpdateCheck.cs` (new, pure + tested by `ReleaseUpdateCheckTests.cs`), `Features/About/AboutController.cs`, `Controls/KillerDialog.cs` (`checkboxInitial`), `Shell/About.cs` | **Already in Scalpel** - `MainWindow.Update.cs` (`EnsureUpdateOptIn`, `CheckForUpdatesAsync`, throttle `LastUpdateCheck`, per-version dismiss) + `Services/UpdateService.cs` driven by `website/public/version.json`; Settings toggle `UpdateCheckToggle`. Scalpel opens the download URL rather than self-swapping, so the dirty-document guard and prerelease filter are moot. |
| F2 | **Ctrl+R / Ctrl+Shift+R** rotate selected pages CW / CCW; gesture text shown in the page context menu and shortcut table | `df125dce`; `Shell/KeyboardShortcuts.cs`, `Shell/ContextMenu.cs` (`MakeRotateMenuItem` gets gesture), `Services/ShortcutTable.cs` | **Applies - port (S)**. Scalpel has `RotatePages_Click(int delta)` (`MainWindow.ContextMenu.cs:97`) but no `Key.R` binding in `MainWindow.KeyboardShortcuts.cs`. Add an `else if (e.Key == Key.R && (Ctrl or Ctrl+Shift))` branch (ignore `e.IsRepeat`), a row in the F1 shortcut overlay, and gesture text on the rotate menu items. |
| F3 | **Global keyboard-shortcut toggle** (#405): checkbox in the shortcuts panel, `Ctrl+Shift+K` always toggles; when off, every non-text-input key except Tab/Space/Enter/modifiers is swallowed; outline tree keys also gated; status-bar confirmation | `fc30ab7f`, `5c26497a` (moved beside online guide); `Services/ShortcutTogglePolicy.cs` (pure, `ShortcutTogglePolicyTests.cs`), `Shell/ShortcutToggle.cs`, `Shell/KeyboardShortcuts.cs`, `Shell/SidebarOutline.cs` | **Applies - port (S/M)**. Nothing like it in Scalpel (`grep ShortcutsEnabled` = 0). Scalpel has single-letter tool keys (V/T/H/D/L/I/C/G, digits) in `MainWindow.KeyboardShortcuts.cs:289-317`, the exact case users want to disable. Port the pure policy class into `Services/`, link it in `Scalpel.Tests`, gate at top of `OnPreviewKeyDown` and in `MainWindow.Outline.cs` key handler, persist `KeyboardShortcutsEnabled` via `App.SetSetting`, add a Settings checkbox + locale keys in all 9 files. |
| F4 | **Blank-page size dialog** (#400): presets Current page / Letter / A4 / Legal / Custom (inches, 0.1-200 validated), used by both "insert after" and "add at end" | `ff623415`; `Controls/BlankPageDialog.cs` (new), `Shell/PageOperations.cs` | **Applies - port (S/M)**. Scalpel hardcodes A4: `MainWindow.FileToolbar.cs:353` (`new PdfPage { Width = 595, Height = 842 }`), and the context-menu label "Insert Blank Page After" is hardcoded English (`MainWindow.ContextMenu.cs:82`). Build the dialog with `ShowToolForm`/`ToolField` (already used in `MainWindow.Tools.cs`); "Current page" = `_doc.Pages[i].Width/Height` swapped when `RotationOf(i)` is 90/270. |
| F5 | **Open Containing Folder** in the document-tab context menu (#399), plus Close Tab / Close Other Tabs with gestures | `2ab9aa6f`; `Controls/Viewer/PdfViewer.TabStrip.cs` (`explorer.exe /select,"path"`) | **Applies - port (S)**. Scalpel tab chips (`MainWindow.Tabs.cs:231 BuildTabChip`) have no context menu at all; `explorer.exe` is only used for the log folder (`MainWindow.Settings.cs:86`). Attach a themed `ContextMenu` to the chip: Open Containing Folder (disabled if `!File.Exists`), Copy Path, Close Tab, Close Other Tabs (`CloseOtherTabs()` exists). |
| F6 | **`--batch-render` CLI**: renders first N pages of a PDF or folder tree to PNG at a size cap, CSV per-page timing log, exit codes 0/1/2; `validation/Benchmark-Versions.ps1` gains `-Mode Render` | `9cc30873`, `7b62b8ca`; `Features/Cli/BatchRenderRunner.cs`, `Features/Cli/CliRunner.cs` | **Applies - port, optional (M)**. Upstream's maintenance runner is built on Docnet, which Scalpel already ships. Useful only as a dev/perf regression tool. Scalpel has no CLI runner; needs an `AttachConsole` shim (WinExe), must route every Docnet call through `PdfiumGate.Run`, and must run before the single-instance forwarding in `App.xaml.cs`. |
| F7 | Vietnamese application, installer, OCR catalog and website localization (#416) | `a65ae02c`, `393e0f66`, `8289b814`; `Strings/vi-VN.xaml` (1051 lines, bulk skipped), `Services/LocaleManager.cs`, `Services/OcrCatalog.cs` | **Not applicable** - Scalpel's locale set is different (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru); the file could seed a Scalpel `vi` locale later but keys differ. |
| F8 | Localized installer wizard + portable launcher (948-line `Strings.xml`, `LauncherStrings.cs`, `Test-Localization.ps1`) and remaining dialog strings (password prompt, About/cert card, install dialog buttons) | `9bd21d28`, `648662a0`; `Packaging/KillerLauncher/*`, `App.xaml.cs`, `Controls/KillerDialog.cs` | **Applies - port (M)** as a localization sweep. Scalpel still has English literals: `Services/InstallerUI.cs` has zero `Loc()`/`FindResource("Str_` calls (e.g. "Create desktop shortcut" line 51); password prompt `MainWindow.FileOps.cs:757` (`"... is password protected."`); 19 literal `MakeMenuItem("...")` in `MainWindow.ContextMenu.cs`; Crop bar "CropBox (pts):" (`MainWindow.Crop.cs:185`). Use the placeholder-split technique upstream used (`{0}` in the template, filename run bolded) so RTL locales can reorder. |
| F9 | About panel layout: options evenly spaced, startup-update toggle at bottom, Clear-all-Data aligned | `bf890616`, `5c26497a` | **Not applicable** (Scalpel's About/Settings are laid out differently; ribbon + Settings panel). |

---

#### 2. Bug fixes

| # | Item (issue) | Upstream commits / files | Scalpel verdict |
|---|------|--------------------------|-----------------|
| B1 | **Per-tab scroll and zoom isolation (#399)**: session now stores `ScrollH/ScrollV`; persisted `DocStates` registry value extended to `path\|fit\|zoom\|view\|page\|scrollH\|scrollV` (backward compatible parse); reopening a file restores its own fit mode (no longer overridden by the global DefaultFitMode) and exact offsets; `BootstrapDocumentView` captures `expectedSession` and every deferred Loaded/Background/ContextIdle callback bails if the active tab changed | `e8a3b77c`; `Controls/Viewer/PdfViewer.Tabs.cs`, `PdfViewer.TabsApi.cs`, `PdfViewer.Viewport.cs`, `Shell/FileOperations.cs`, `MainWindow.xaml.cs` | **Applies - port (M)**. Scalpel's `MainWindow.Tabs.cs` keeps `TabViewState(PageIndex, Zoom, Mode)` in memory only: no scroll offsets (continuous view lands at page top, not where you were), no `FitMode` (a fitted tab comes back as a fixed zoom), nothing persisted across restarts (`grep DocStates` = 0), and `RestoreTabState` queues a `Loaded` callback with no check that the same tab is still active, so fast Ctrl+Tab can apply tab A's zoom/page to tab B. Port: add `ScrollH, ScrollV, FitMode` to the record, capture `PagePreviewPanel.HorizontalOffset/VerticalOffset` in `RememberTabState`, restore at `ContextIdle` after zoom, guard every deferred step with `PathEq(_originalFile, path)`; optional registry persistence (cap ~40 entries, `\|`-delimited). |
| B2 | #399 follow-up: snapshot the tab's scroll offsets **before** rebuilding the viewport, because layout-only `ScrollChanged` events during the rebuild overwrote the incoming tab's saved position | `6546f1db`; `PdfViewer.Tabs.cs` | **Applies - port (fold into B1)**. Scalpel captures state explicitly in `RememberTabState` rather than from `ScrollChanged`, so the exact race does not exist today; if B1 adds a `ScrollChanged` writer, copy this snapshot-first rule. |
| B3 | Continuous view: a fresh Fit Width / Fit Page (not a tab restore) resets the horizontal offset to 0, twice (now and again at `Loaded`) because WPF briefly keeps the old extent | `a8cc4973`; `PdfViewer.Viewport.cs` (`ResetContinuousFitHorizontalOffset`) | **Needs verification (S)**. Scalpel's continuous branches in `MainWindow.Zoom.cs` `FitToWidth`/`FitToPage` never touch `HorizontalOffset`. Repro: zoom to 300% in Continuous, scroll right, press Fit Width; if the strip is left-clipped, add the same two-step `ScrollToHorizontalOffset(0)`. |
| B4 | **Annotations misplaced on cropped pages (#418)** when saving or printing: burn transform now offsets by the effective page box origin (`X`,`Y`) | `05f15430`; `Services/PdfEngineBurn.cs` (`ApplyVisualTransform(x, y, ...)`), engine `PdfPageInformation.X/Y` | **Needs verification (M), likely applies**. Scalpel crops by writing `/CropBox` (`MainWindow.Crop.cs:591`), PDFium renders the CropBox, but `DrawAnnotationsOnDocument` (`MainWindow.SaveAnnotations.cs:133-140`) builds `XGraphics.FromPdfPage` + `BurnRotation.For(..., gfx.PageSize ...)` from the MediaBox, with no CropBox offset anywhere in SaveAnnotations/BurnRotation. Test: crop a page off-center, add text, save, reopen. Fix: prepend `XMatrix` translate of (crop.x1 - media.x1, media.y2 - crop.y2) and pass crop width/height to `BurnRotation.For`; same for the print burn path. |
| B5 | **Annotations silently dropped when cached render dimensions are missing**: burn now falls back to canonical dims computed from the page size instead of skipping the page | `689e9f6b`; `Services/PdfEngineBurn.cs` (`CanonicalRenderDimensions`), `AnnotationSaveDiagnosticTests.cs` | **Applies - port (S)**. Same bug in Scalpel: `MainWindow.SaveAnnotations.cs:104` `if (!_renderDims.ContainsKey(pageIdx)) continue;` drops the whole page's annotations with no log. `_renderDims` is populated in `MainWindow.FileOps.cs:935/1122` (DIP size of the rendered page); fallback = the same DIP formula from `page.Width/Height` (rotation-aware), plus a `Logger.Warn("Save", "renderdims.missing")`. |
| B6 | Page extraction / split / selected-page export ignore malformed optional `/Thumb` images (#423) | `2da1b5aa`; `Services/PdfEngineIntegration.cs`, engine `PdfIncrementalPageEditor.ClearPageThumbnail` | **Needs verification (S)**. Depends on whether PdfSharpCore's `ImportExternalPage` deep-copies `/Thumb` and chokes on a bad stream. Add a test PDF with a broken `/Thumb`; if extraction fails, remove `/Thumb` from imported pages (`newPage.Elements.Remove("/Thumb")`) in the split/extract code in `MainWindow.FileToolbar.cs`. |
| B7 | Selected-page Transform survives inconsistent `/FirstChar /LastChar /Widths` font metadata (compaction step made optional) | `65cce2ca`; `Services/PdfEngineIntegration.cs` (`IsImportedFontWidthFailure`) | **Not applicable** - failure is inside upstream's engine rebuild/compaction; Scalpel's `Services/TransformService.cs` uses PdfSharpCore/Docnet and has no such step. |
| B8 | **Tagged PDFs: markup saves again** (engine "described overlays" extend the structure tree), and **markup is finished in a temp file before the destination is overwritten** | `8c927768`; `Services/PdfEngineBurn.cs`, `Shell/FileOperations.cs`, engine `PdfIncrementalPageEditor` | Split verdict. Structure-tree part: **Not applicable** (PdfSharpCore `XGraphics` append never validates `/StructTreeRoot`, so Scalpel never had the failure; burned content is simply untagged). Atomic-write part: **Applies - port (S)** - `Services/PdfSaveGuard.Save` calls `doc.Save(path)` straight onto the user's file and then rewrites it in place (`RewriteProducer`, `StripSavedTransparencyGroups`); a throw mid-way leaves a truncated original (`MainWindow.FileToolbar.cs:416`, `:426`, `:507`, `:553`). Save to a sibling temp and `File.Replace`/`File.Copy(overwrite)` at the end. |
| B9 | Black/unpainted window after minimize or taskbar switch (#415): `WM_ERASEBKGND` only suppressed during a live size/move | `2f2487ba`; `Shell/WindowChrome.cs` | **Not applicable** - Scalpel's `WndProc` (`MainWindow.Settings.cs:205`) does not intercept `WM_ERASEBKGND` or `WM_NCCALCSIZE`. |
| B10 | Windows 11 taskbar button missing the icon after slow portable starts: re-assign `Icon` on `Loaded` | `37e08085`; `MainWindow.xaml.cs` | **Needs verification (S)**. Scalpel sets `Icon=` in `MainWindow.xaml` line 1 only. Costura cold starts can be slow; if the taskbar ever shows the generic icon, add `Loaded += (_, _) => Icon = new BitmapImage(new Uri("pack://application:,,,/Resources/scalpel.ico"))`. |
| B11 | Cross-monitor maximize uses the destination monitor (#363): (a) `WM_NCCALCSIZE` uses `MonitorFromRect(proposed)`; (b) `WM_GETMINMAXINFO` uses the cursor's monitor while inside a size/move loop | `749c9834`, `25aed961`; `Shell/WindowChrome.cs` | (a) **Not applicable** (no `WM_NCCALCSIZE` handler in Scalpel). (b) **Needs verification (S)** - Scalpel's `WmGetMinMaxInfo` (`MainWindow.Settings.cs:~294`) uses `MonitorFromWindow` and also clamps `ptMaxTrackSize` to that monitor, so Aero-snap/maximize after dragging onto a monitor of a different size/DPI may use the source's work area. Port: track `WM_ENTERSIZEMOVE/EXITSIZEMOVE`, use `MonitorFromPoint(GetCursorPos())` while inside. The user's two identical QHD monitors will not reproduce it. |
| B12 | Localized Fit Page / Fit Width entries clipped in the zoom box (#419): width clamp raised 92 -> 220, caret collapsed to the start | `5da1654d`; `Shell/SettingsPanel.cs` (`AdjustZoomBoxWidth`), `MainWindowViewerHost.cs` | **Applies - port (S)**. Scalpel's `ZoomBox` is fixed `Width="92"` (`MainWindow.xaml:1050`); long labels such as tr "Genişliğe Sığdır", ru "По странице", es "Ajustar página" are at risk. Measure the longest item with `FormattedText` after every locale switch (`LocaleManager` apply / `Lang*Radio_Checked`) and clamp 70..220. |
| B13 | Document Info page count / Producer label localized (#394, #407); keyboard-map layer captions (CTRL/SHIFT/ALT) localized and rebuilt on language change; bookmark "add" ghost row included in sidebar width auto-fit (#394) | `19357d28`, `151cce7b`; `Controls/DocumentInfoDialog.cs`, `Shell/KeyboardMapOverlay.cs`, `Shell/SidebarOutline.cs` | Document Info: **Already in Scalpel** (`MainWindow.Tools.cs:251-252` uses `Str_DocInfo_Producer` / `Str_DocInfo_Pages`). Keyboard map layers and ghost row: **Not applicable** (Scalpel has an F1 shortcut overlay, no layered keyboard map; `MainWindow.Outline.cs` has no add-bookmark ghost row). |
| B14 | Legacy "Open with" entries (`Applications\KillerPDF.exe` and `KillerPDF.App.exe`) both launch the installed payload, not the installer (#393) | `b9f56b96`; `App.xaml.cs` | **Not applicable** - Scalpel is one EXE with no launcher/payload split; `Services/Installer.cs:44` registers `Applications\Scalpel.exe` only. |
| B15 | Standalone uninstall dialogs forced to the default Dark/Green palette; install errors (unsigned, downgrade, copy failure, generic failure) use themed `KillerDialog` instead of `MessageBox` | `9a9fbdbd`; `App.xaml.cs`, `Services/ThemeManager.cs` (`InitializeInstallerTheme`) | Uninstall palette: **Already in Scalpel** (`Services/InstallerUI.cs` fixed dark+amber palette, `RunUninstallFlow`). Install errors: **Applies - port (S)** - Scalpel still uses raw `MessageBox.Show` in `App.xaml.cs` at lines 787, 1139, 1167, 1181, 1253 (same messages as upstream's old code). Route them through `InstallerUI`/`ScalpelDialog`. |
| B16 | WinGet manifests run the installer and declare the desktop runtime dependency (#386) | `7ad1b478`, `7458c913`, `7bd49d58`, `4695b3ab`, `a53bf5e8`, `7fd1bbed`, `d371ceb6`; `build/New-WinGetManifest.ps1`, `.github/workflows/winget-release.yml` | **Not applicable** - Scalpel does not publish to WinGet (portable, Inno, Store). |
| B17 | **1.8.61: installable packages restored** after `AssemblyInfo.cs` carried a stale `[AssemblyVersion("1.8.5.0")]` that disagreed with the csproj; `build-portable.ps1` now asserts the payload assembly version equals the project FileVersion and smoke-runs `KillerPDF.App.exe --version` | `3bb4d25c`; `AssemblyInfo.cs`, `KillerPDF.csproj`, `build/build-portable.ps1`, `release.ps1` | Root cause: **Already in Scalpel** (all three version fields live only in `Scalpel.csproj:13-15`; `AssemblyInfo.cs` has no version attribute). Gate idea: **Needs verification (S)** - `release.ps1:280` reads the EXE FileVersion but does not assert it equals the csproj `<Version>`; a one-line throw would catch the same class of drift. |
| B18 | Italian translation corrections (#395, #407, #408) | `ca4f4ca6`, `9d88c933`, `c3ee656a`, `b51c2582`, `7f708e6a`, `97d508ff`, `ade0fccb` | **Not applicable** (no Italian locale in Scalpel). |
| B19 | **Page-size normalization stays inside 3..14400 pt (#401)**: when one edge is too small and the other too large no uniform scale can fix it, so the page is left byte-for-byte instead of rewritten still invalid; also validates emitted corner products | `dd13c9b4` (contributor), `9c05f40c`; engine `Writing/PdfPageDimensionNormalizer.cs` + tests | **Applies - port (S)**. Scalpel's `Services/PdfSaveGuard.ScaleFactorToRange` shrinks by `Max/maxEdge` and then only grows if safe, so e.g. 20000 x 2 pt returns 0.72 and produces 14400 x 1.44 (short edge made worse, still out of range). Return 1.0 when the result is still out of range, and add the upstream cases to `Scalpel.Tests`. The float-corner nudge is not needed (PdfSharpCore writes width/height, not independent corners). |
| B20 | Signing (#381): incremental signature keeps the source xref format (table vs stream); visible appearance allowed on an **existing** empty signature field (writes `/AP /N` on the field or its widget kids); sequential existing-field signatures covered by tests | `12d3d9d2`, `f24b6c80`; engine `Signing/PdfDetachedSignatureWriter.cs`, `PdfSignatureOptions.cs` | **Not applicable** today - `Services/PdfSigningService.cs` requires a classic xref (throws on xref streams, line 761; source comes from `BuildWorkingSourceFile`, a PdfSharpCore re-save) and always creates a new `/FT /Sig` field (line 221). Noted as a feature gap: signing into a pre-existing empty signature field. |

---

#### 3. Optimizations, refactors, architecture, tests, tooling

| # | Item | Upstream commits / files | Scalpel verdict |
|---|------|--------------------------|-----------------|
| O1 | Update check extracted to a pure `ReleaseUpdateCheck` (`FindNewerReleaseAsync` + `ParseNewerRelease`) with unit tests; `_updateInProgress` re-entrancy guard; temp download always deleted in `finally` | `44b776aa`; `Services/ReleaseUpdateCheck.cs`, `ReleaseUpdateCheckTests.cs` | **Already in Scalpel** - `Services/UpdateService.cs` is the equivalent pure service. |
| O2 | Stale-callback guards: `expectedSession` capture in `BootstrapDocumentView` and a `_tabViewportRestoreGate` generation counter so deferred dispatcher work for a tab you already left is dropped | `e8a3b77c`, `6546f1db` | **Applies - port (part of B1)**. Scalpel's `RestoreTabState` has no guard. A cheap generation int incremented on every tab switch is enough. |
| O3 | Save pipeline reorder: clean copy -> edited temp -> burn/forms/rotations -> single copy to destination | `8c927768` | **Applies - port (S)**, see B8. |
| O4 | Localization parity test now scans source for hardcoded UI literals (`Text=`/`Content=`/`Header=`/`ToolTip=`/`Title=`, `new Run("...")`, `UiKit.Make("...")`, `UiKit.CheckBox("...")`) with an allow-list, plus per-file banned-literal checks and an #407 regression test | `648662a0`, `151cce7b`; `KillerPDF.Tests/LocalizationParityTests.cs` | **Applies - port (S)**. `Scalpel.Tests/LocaleParityTests.cs` has only 3 tests (files present, keys + placeholders, no duplicates). A literal scanner would have flagged the F8 findings; start it as a report-only/allow-listed test since Scalpel has many literals today. |
| O5 | Pure policy helpers with tests for input handling (`ShortcutTogglePolicy`) | `fc30ab7f` | **Applies - port (with F3)**. Matches Scalpel's existing `Services/EditableTextShortcutPolicy.cs` pattern. |
| O6 | `.github/maintenance-forward-ports.json` (~40 records: maintenance SHA -> development SHA + reason) and `build/Check-MaintenanceForwardPorts.ps1` (read-only ancestry check, exits 1 on an unported maintenance commit), synced heavily in this range | `2710273b`, `f8f1a872`, `bf4673f0`, `28d0e8f6` | **Not applicable** (Scalpel has one branch). Idea worth copying: a Scalpel-side JSON ledger of upstream SHA -> Scalpel verdict/commit would make these triage rounds incremental instead of re-reading history. |
| O7 | Corpus benchmarks published for 1.8.4 and 1.8.6 (5 passes each on 16,696 / 649 / 29,599 files): 1.8.6 medians 18.2% faster than 1.8.4 on the regression set, 1.9% on standards, 3.4% on stress, zero crashes/timeouts on the damaged-file set; no single commit is credited | `cae40389`, `0dbd5ebb`; `validation/PERFORMANCE.md`, `validation/RESULTS.md`, `validation/benchmarks/1.8.x/*` | **Not applicable** (measures the upstream engine). The approach (fixed corpus + CSV medians per release) pairs with F6 if Scalpel ever wants render/save perf tracking. |
| O8 | Build gate: payload assembly version must equal project FileVersion + startup smoke test; the engine-vs-app version-lock MSBuild target and release check removed | `3bb4d25c`; `build/build-portable.ps1`, `engine/.../KillerPdf.Engine.csproj`, `release.ps1` | Version assert: **Needs verification (S)**, see B17. Engine lock removal: **Not applicable**. |
| O9 | `build/payload-files.txt` runtime inventory refresh (.NET 10 servicing DLL name) | `cd063862` | **Not applicable** (net48, Costura single file). |
| O10 | `KillerDialog.ShowWithCheckbox(..., checkboxInitial)` so a dialog checkbox can reflect a stored setting | `eaa94748`; `Controls/KillerDialog.cs` | **Not applicable** on its own (Scalpel's update prompt is an overlay, not a checkbox dialog); only needed if F1's in-prompt toggle is copied. |
| O11 | `IAboutHost.IsDirty` now considers every tab/pane session, not only the active one | `44b776aa`; `Shell/About.cs` | **Not applicable** - Scalpel never self-replaces the EXE, so no dirty check is needed before update. |

---

#### 4. Engine (KillerPdf.Engine) changes - brief

All engine code is net10 and not portable; behaviours are mapped above.

| Engine change | Commit | Mapped to |
|---------------|--------|-----------|
| `PdfPageInformation.X/Y` (effective page-box origin) for cropped-page overlays (#418) | `05f15430` | B4 |
| `PdfDetachedSignatureWriter`: keep source xref format; visible `/AP` on existing signature fields and widget kids (#381) | `12d3d9d2`, `f24b6c80` | B20 |
| `PdfPageDimensionNormalizer`: feasibility check, corner-product validation, 14-step nudge, no-op when infeasible (#401) | `dd13c9b4` | B19 |
| `PdfIncrementalPageEditor.AppendPageDescribedContent`: described overlays that extend existing structure trees | `8c927768` | B8 |
| `PdfIncrementalPageEditor.ClearPageThumbnail` / `RemoveThumbnail` on imported pages (#423) | `2da1b5aa` | B6 |
| Engine NuGet workflow adjustments and the 1.8.6 engine changelog/date | `3bb4d25c`, `168f8987`, `1b0cac2b` | n/a |

---

#### 5. Localization, website, packaging, CI - brief

| Area | Commits | Scalpel verdict |
|------|---------|-----------------|
| Vietnamese app/installer/OCR/website/engine docs (#416) - bulk content skipped | `a65ae02c`, `393e0f66`, `8289b814` | Not applicable (see F7) |
| Italian corrections (#395, #407, #408) | see B18 | Not applicable |
| New string keys across all 15 upstream locales (shortcut toggle, startup update, blank page, open folder, About/cert labels, password prompt) | `fc30ab7f`, `44b776aa`, `eaa94748`, `ff623415`, `2ab9aa6f`, `648662a0`, `151cce7b` | Port alongside F2-F5/F8; remember Scalpel needs every key in all 9 locale files |
| Landing site: release info per version, translated guides refresh (1,314-line `kp-i18n.js` diff), rotation shortcuts docs, download layout (KillerTerminal card), About bio text, scrollbar arrows removed, stylesheet cache-busting, corpus pages, locale-count fix | `0e38d353`, `b0a526ab`, `fec291f6`, `df125dce`, `37ed75a0`, `18082bfa`, `f4bdb82c`, `28ccdc86`, `e2b3bd5c`, `c7eafbf7`, `17a3c8b8`, `2676767c` | Not applicable (separate Scalpel website in `website/`) |
| README source links per version; Chocolatey nuspec description refresh | `6955e44e`, `378606fa`, `3efe6a69`, `fec291f6` | Not applicable |
| WinGet manifest generator + workflow (fork sync via merge API, schema, CRLF, validator) (#386) | see B16 | Not applicable |
| Version opening/dating commits (1.8.5 open, release dates) | `7c07c4f3`, `21f72c86`, `1b0cac2b` | Not applicable |
| `release.ps1` landing-field repair and WinGet sync pre-check | `b0a526ab`, `7458c913` | Not applicable |

---

#### Verdict counts (per item, split verdicts counted once per part)

| Verdict | Count | Items |
|---------|------:|-------|
| Applies - port | 17 rows (11 distinct work items) | F2, F3 (+O5), F4, F5, F6 (optional), F8, B1 (+B2, O2), B5, B8 atomic save (+O3), B12, B15 install errors, B19, O4 |
| Already in Scalpel | 5 | F1, B13 (Doc Info), B15 (uninstall palette), B17 (version single source), O1 |
| Needs verification | 6 | B3, B4, B6, B10, B11 (b), B17/O8 (release version assert) |
| Not applicable | 16, plus all engine-only, website, CI and packaging rows | F7, F9, B7, B8 (structure tree), B9, B11 (a), B13 (kb map, ghost row), B14, B16, B18, B20, O6, O7, O9, O10, O11 |

#### Top 5 worth porting

1. **B1 + B2 + O2 - per-tab scroll/fit/zoom state with stale-callback guard** (M). Biggest everyday UX gain in Scalpel's tab strip; optionally persist per-file state across restarts.
2. **B5 - stop dropping annotations when `_renderDims` is missing** (S). Silent data loss on save in `MainWindow.SaveAnnotations.cs:104`.
3. **B4 - verify and fix annotation offset on cropped pages** (M). Scalpel writes `/CropBox` itself, and the burn path ignores the CropBox origin.
4. **B8 atomic save + B19 page-size normalization no-op** (S each). Don't write straight over the user's file, and don't "fix" a page into a worse invalid size.
5. **F3 shortcut toggle (Ctrl+Shift+K) + F2 Ctrl+R rotate + F5 tab context menu with Open Containing Folder** (S each). Cheap, self-contained, user-visible wins.
