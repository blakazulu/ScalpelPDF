# Design Spec — Sub-project R: MainWindow Monolith Split

**Date:** 2026-06-28
**Status:** Approved (design), pending implementation plan
**Author:** brainstormed with Claude Code

---

## Program context (the bigger picture)

This refactor is the **foundation sub-project** of a larger program: porting ~10 features from
upstream KillerPDF v1.6.0 into ScalpelPDF, each rebuilt in Scalpel's own "Clinical" design language
(ribbon UI, theme tokens, `_Shared.xaml` styles, RTL-aware, localized across all 9 locale files,
with a `Services/Changelog.cs` entry).

The user chose **"refactor first, fully"** — split the monolith before any feature work — so that each
feature later drops into clean, focused files rather than growing the monolith further.

**Each item below is its own future spec → plan → build cycle. This document specs only R.**

Program roadmap (sequencing decided; details deferred to per-feature specs):

- **R — Monolith split** ← *this spec*
- **Tier 1 (small wins):** Line tool · Document Info dialog (F12) · Full-screen (F11) + missing keyboard shortcuts
- **Tier 2 (medium):** OCR UX (clipboard page/region, Extract-All-Text, language picker, HQ toggle) ·
  RGB color picker w/ eyedropper · Recent files · Watermark/image stamps · Form-filling polish
- **Tier 3 (large/strategic):** Cryptographic PKI signing · Tabbed documents · Transform tool (deskew/scale/flip)

---

## Goal

Split `MainWindow.xaml.cs` (9,738 lines) into ~30 focused `MainWindow.<Area>.cs` partial-class files
with **zero behavior change**. This is purely mechanical relocation of complete members. No logic edits,
no service extraction, no renames, no signature changes, no dead-code removal.

## Why now

- `MainWindow.xaml.cs` is the architectural bottleneck (CLAUDE.md calls it "the monolith"). Every feature
  port would otherwise enlarge it.
- The file is already organized into 33+ banner-comment sections (`// ====`), which are near-perfect,
  low-risk seams for a partial-class split.
- Moving complete members between files of the same `partial class MainWindow` cannot change behavior if
  it compiles — making this the safest possible large refactor.

## Non-goals (explicitly out of scope for R)

- **No service extraction.** Pulling logic into testable `Services/` units is deferred to the per-feature
  cycles where it pays off. R is pure partial-class relocation.
- **No `MainWindow.xaml` changes.** XAML cannot be partial-split cleanly; styles already live in
  `_Shared.xaml`. The 1,447-line XAML is left untouched.
- **No cleanups of opportunity.** No renaming, no reordering logic, no removing dead code, no fixing
  smells. Anything beyond a pure move is a separate change so the diff stays verifiable.
- **No field co-location (yet).** All `_`-field declarations stay in the core file for R (see below).
- `MainWindow.Tools.cs` and `MainWindow.Update.cs` already exist as partials — left as-is.

## Design

### Core file: `MainWindow.xaml.cs` (slim)

Retains:
- `using` directives, namespace, `public partial class MainWindow : Window` declaration
- **All `_`-field declarations** — kept here for R to eliminate every ambiguity about field ownership and
  keep each move a pure cut/paste of methods. (Co-locating fields with their feature is a deferred
  nice-to-have, not part of R.)
- The constructor + `InitializeComponent` wiring
- The `WM_GETMINMAXINFO` / maximize-respects-taskbar window-init fix (tightly coupled to window startup)

### Partial files (one per section; tiny adjacent sections merged)

Each becomes `public partial class MainWindow` in the project root. Mapping from current banner sections
(source start line → target file):

| Source section (start line)                         | Target file                          |
|-----------------------------------------------------|--------------------------------------|
| Settings panel (313) + Settings persistence (680)   | `MainWindow.Settings.cs`             |
| Window chrome (624)                                 | `MainWindow.WindowChrome.cs`         |
| Context menu (765)                                  | `MainWindow.ContextMenu.cs`          |
| File operations (871)                               | `MainWindow.FileOps.cs`              |
| PDF Link Annotation Overlays (2023)                 | `MainWindow.Links.cs`                |
| PDF Form Field Overlays (2250)                      | `MainWindow.Forms.cs`                |
| App mode View/Edit/Pages/Sign (3194)                | `MainWindow.AppMode.cs`              |
| Tool selection (3222)                               | `MainWindow.ToolSelection.cs`        |
| Sidebar outline/bookmark panel (3336)               | `MainWindow.Outline.cs`              |
| Crop tool (3518)                                    | `MainWindow.Crop.cs`                 |
| Draw/Highlight settings bar (4179)                  | `MainWindow.DrawBar.cs`              |
| Text tool settings bar (4365)                       | `MainWindow.TextBar.cs`              |
| Signatures (4497)                                   | `MainWindow.Signatures.cs`           |
| Canvas interaction (5107)                           | `MainWindow.CanvasInteraction.cs`    |
| Selection (5681)                                    | `MainWindow.Selection.cs`            |
| Search Ctrl+F (6011)                                | `MainWindow.Search.cs`               |
| Inline text editing (6320)                          | `MainWindow.InlineTextEdit.cs`       |
| Text box handling (6772)                            | `MainWindow.TextBox.cs`              |
| Keyboard shortcuts (6902)                           | `MainWindow.KeyboardShortcuts.cs`    |
| Annotation management (7052)                        | `MainWindow.AnnotationManagement.cs` |
| Dirty tracking (7330) + Close file (7345)           | `MainWindow.DirtyTracking.cs`        |
| File toolbar handlers (7400)                        | `MainWindow.FileToolbar.cs`          |
| Save annotations to PDF (8151)                      | `MainWindow.SaveAnnotations.cs`      |
| Bitmap rotation helper (8357) + Temp save/reload (8400) | `MainWindow.TempReload.cs`        |
| Zoom (8505)                                         | `MainWindow.Zoom.cs`                 |
| Drag/drop file open (8979) + page reorder (9001)    | `MainWindow.DragDrop.cs`             |
| Page selection handler (9050)                       | `MainWindow.PageSelection.cs`        |
| View Mode (9316)                                    | `MainWindow.ViewMode.cs`             |
| Themed dialog / MessageBox replacement (9597)       | `MainWindow.Dialogs.cs`              |

~29 partial files + slim core. Final grouping of micro-sections may adjust during execution (the rule:
no file smaller than a section's worth of cohesive members; never split a single section across files).

### Project wiring

- `Scalpel.csproj` is SDK-style with **default compile globbing** — new `MainWindow.*.cs` files are
  auto-included. **No csproj edits needed.**
- `Scalpel.Tests` and `Scalpel.E2E` do not link `MainWindow*.cs` (they link `Services/*` and model files),
  so the split does not touch test project references.

## Process

1. Extract **one section at a time** into its target file.
2. `dotnet build` (via `~/.dotnet/dotnet.exe`) after each extraction; fix only compile errors caused by
   the move (there should be none for a clean cut/paste).
3. Commit in logical batches so history stays bisectable and each diff reads as a pure move.
4. Expect the `pdfium.dll` lock gotcha — close any running `Scalpel.exe` before building.

## Verification (acceptance criteria)

After the full split:

- `git diff` shows **only relocation** — no net added/removed logic lines (member bodies character-identical).
- `dotnet build` — clean, no new warnings.
- `dotnet test` — full xUnit suite green.
- e2e / screenshot harness — runs and matches baseline.
- Manual smoke pass — open, edit (text/draw/highlight), save, print, OCR, switch theme, switch locale.
- Core `MainWindow.xaml.cs` reduced to fields + ctor + window-init only (~a few hundred lines).

## Risks & mitigations

| Risk                                                | Mitigation                                              |
|-----------------------------------------------------|---------------------------------------------------------|
| Accidental edit during a cut/paste                  | Per-move build + final test/e2e/smoke; character-identical-body check on diff |
| A field is actually only used by one section and "wants" to move | Deferred by design — all fields stay in core for R |
| A method spans two sections / shared helper          | Keep shared helpers in core or the most-used section; never duplicate |
| pdfium.dll build lock                                | Close running Scalpel.exe before building               |
| Merge churn against in-flight feature branches       | None expected — work on main, land R before feature cycles |

## Definition of done

All ~29 partial files created, core file slimmed, every acceptance criterion above met, changes
committed. No feature behavior added or changed. Ready to begin Tier 1 feature sub-projects.
