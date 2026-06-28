# MainWindow Monolith Split — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `MainWindow.xaml.cs` (9,738 lines) into ~29 focused `MainWindow.<Area>.cs` partial-class files with zero behavior change.

**Architecture:** Pure partial-class relocation. Each banner-comment section (`// ====`) in the monolith moves verbatim into its own file declaring `public partial class MainWindow`. All `_`-fields, the constructor, and the window-init/maximize fix stay in a slim core `MainWindow.xaml.cs`. Because every member stays in the same partial class, there are no cross-file interfaces and no call-site changes — the only failure mode is a botched cut/paste, which the per-file build gate catches.

**Tech Stack:** C# / .NET Framework 4.8 (`net48`), WPF, SDK-style `Scalpel.csproj` with default compile globbing. Build via `~/.dotnet/dotnet.exe`.

## Global Constraints

- **Zero behavior change.** No logic edits, renames, signature changes, reordering, dead-code removal, or service extraction. Moves only.
- **Character-identical bodies.** Member bodies must be byte-for-byte identical after the move (verify with `git diff` showing only relocation).
- **No `MainWindow.xaml` changes.** XAML is untouched.
- **All `_`-fields stay in core** `MainWindow.xaml.cs`. Only methods/properties/nested-types within a section move.
- **No `Services/Changelog.cs` entry.** This is an internal refactor, not a user-facing change — the changelog rule does not apply.
- **Do not modify `Scalpel.csproj`** — default globbing auto-includes new `MainWindow.*.cs` files.
- **Leave existing partials alone:** `MainWindow.Tools.cs`, `MainWindow.Update.cs`.
- **pdfium lock gotcha:** close any running `Scalpel.exe` before building, or the build fails copying `pdfium.dll` (`MSB3027`/`MSB3021`) — that is a lock, not a code error.

---

## Conventions (read once, applies to every extraction task)

### Standard partial-file header

Every new `MainWindow.<Area>.cs` file begins with **exactly** this header (the 19 explicit usings copied from the monolith — `ImplicitUsings` covers `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading.Tasks`; unused usings are harmless and produce no warning), and ends by closing the class and namespace:

```csharp
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // <-- extracted section(s) paste here, including their // ==== banner comments

    }
}
```

Note: the extracted file's class line is `public partial class MainWindow` **without** the `: Window` base list — the base is already declared in the core file and the generated `MainWindow.g.cs`. Restating it is unnecessary.

### The Extraction Procedure (each task is one instance of this)

For target file `MainWindow.<Area>.cs` and one or more section banner titles:

1. **Locate the section.** Find its banner with a fixed-string search, e.g. `grep -n "<exact banner title>" MainWindow.xaml.cs`. The section is the 3-line banner (`// ====` / `// <title>` / `// ====`) **plus** everything after it up to (but not including) the **next** `// ====` banner line — or, for the final section, up to the class's closing `}`.
2. **Create** `MainWindow.<Area>.cs` with the Standard partial-file header above.
3. **Cut** the section text (including its banner) out of `MainWindow.xaml.cs` and **paste** it inside the class body of the new file. For a task that lists two sections, move both (in their original order).
4. **Verify the move is clean:** the core file no longer contains those members; the new file contains them once; the core file still ends with its original `}` `}` (class + namespace).
5. **Build** (see gate below).
6. **Commit** with the task's message.

### Build gate (run after every extraction)

```bash
~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug
```
Expected: `Build succeeded.` with `0 Error(s)` and no new warnings vs. baseline. If it fails on `pdfium.dll` copy, close `Scalpel.exe` and rerun. If it fails on a C# error, the cut/paste was malformed (e.g. a member split across the boundary) — fix the boundary, do not edit logic.

### Ordering

Extract in the listed order (top-of-file section first). Because cutting shifts later line numbers, **always re-locate each section by its banner title (step 1), never by a stale line number.** Line numbers in this plan are first-extraction hints only.

---

## Task 0: Establish the green baseline

**Files:** none (verification only)

- [ ] **Step 1: Ensure a clean tree and no running app**

```bash
git status --short          # expect: clean (or only untracked docs)
# Close any running Scalpel.exe (Task Manager) so pdfium.dll is unlocked.
```

- [ ] **Step 2: Baseline build**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `Build succeeded.` Record the warning count (the post-split builds must not exceed it).

- [ ] **Step 3: Baseline tests**

Run: `~/.dotnet/dotnet.exe test`
Expected: all tests pass. Record the passing count.

- [ ] **Step 4: Baseline e2e (if the harness runs locally)**

Run the e2e/screenshot harness per the project's usual command (see `Scalpel.E2E`). Expected: green / screenshots captured as the baseline to compare against at the end. If it cannot run in this environment, note that and rely on the final manual smoke pass.

- [ ] **Step 5: Record the core line count**

Run: `wc -l MainWindow.xaml.cs`
Expected: 9738. (Final target after the split: a few hundred — fields + ctor + window-init only.)

---

## Task 1: Extract Settings → `MainWindow.Settings.cs` (worked example)

**Files:**
- Create: `MainWindow.Settings.cs`
- Modify: `MainWindow.xaml.cs` (remove the two Settings sections; first hint ~line 312 and ~line 680)

**Sections to move (by banner title):**
- `Settings panel`
- `Settings persistence (window size, zoom, last file)`

- [ ] **Step 1: Locate both sections**

```bash
grep -n "Settings panel" MainWindow.xaml.cs
grep -n "Settings persistence (window size, zoom, last file)" MainWindow.xaml.cs
```
Expected: each prints the banner title line. Identify each section's span (its `// ====` banner through the line before the next `// ====` banner).

- [ ] **Step 2: Create `MainWindow.Settings.cs` with the Standard partial-file header**

Use the header from Conventions verbatim.

- [ ] **Step 3: Move both sections**

Cut `Settings panel` (banner + body) and `Settings persistence …` (banner + body) out of `MainWindow.xaml.cs` and paste both, in order, inside the class body of `MainWindow.Settings.cs`.

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `Build succeeded.`, `0 Error(s)`, warning count ≤ baseline.

- [ ] **Step 5: Confirm pure move**

```bash
git add -A && git diff --cached --stat
```
Expected: `MainWindow.xaml.cs` shows only deletions, `MainWindow.Settings.cs` shows the matching additions, net logic delta ≈ 0.

- [ ] **Step 6: Commit**

```bash
git commit -m "refactor: extract Settings into MainWindow.Settings.cs (no behavior change)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Tasks 2–29: Extractions

Each task below = one run of **The Extraction Procedure** (Conventions): create the file with the Standard header, move the listed section(s) by banner title, run the Build gate, confirm pure move via `git diff --cached --stat`, then commit with the given message. The build gate after each task is the per-task test.

### Task 2: `MainWindow.WindowChrome.cs`
- Sections: `Window chrome` (hint ~line 624)
- Commit: `refactor: extract window chrome into MainWindow.WindowChrome.cs (no behavior change)`

### Task 3: `MainWindow.ContextMenu.cs`
- Sections: `Context menu` (~765)
- Commit: `refactor: extract context menu into MainWindow.ContextMenu.cs (no behavior change)`

### Task 4: `MainWindow.FileOps.cs`
- Sections: `File operations` (~871)
- Commit: `refactor: extract file operations into MainWindow.FileOps.cs (no behavior change)`

### Task 5: `MainWindow.Links.cs`
- Sections: `PDF Link Annotation Overlays` (~2023)
- Commit: `refactor: extract link overlays into MainWindow.Links.cs (no behavior change)`

### Task 6: `MainWindow.Forms.cs`
- Sections: `PDF Form Field Overlays` (~2250)
- Commit: `refactor: extract form-field overlays into MainWindow.Forms.cs (no behavior change)`

### Task 7: `MainWindow.AppMode.cs`
- Sections: `App mode (View / Edit / Pages / Sign)` (~3194)
- Commit: `refactor: extract app-mode switching into MainWindow.AppMode.cs (no behavior change)`

### Task 8: `MainWindow.ToolSelection.cs`
- Sections: `Tool selection` (~3222)
- Commit: `refactor: extract tool selection into MainWindow.ToolSelection.cs (no behavior change)`

### Task 9: `MainWindow.Outline.cs`
- Sections: `Sidebar outline/bookmark panel` (~3336)
- Commit: `refactor: extract outline panel into MainWindow.Outline.cs (no behavior change)`

### Task 10: `MainWindow.Crop.cs`
- Sections: `Crop tool` (~3518)
- Commit: `refactor: extract crop tool into MainWindow.Crop.cs (no behavior change)`

### Task 11: `MainWindow.DrawBar.cs`
- Sections: `Draw/Highlight settings bar` (~4179)
- Commit: `refactor: extract draw/highlight bar into MainWindow.DrawBar.cs (no behavior change)`

### Task 12: `MainWindow.TextBar.cs`
- Sections: `Text tool settings bar` (~4365)
- Commit: `refactor: extract text settings bar into MainWindow.TextBar.cs (no behavior change)`

### Task 13: `MainWindow.Signatures.cs`
- Sections: `Signatures` (~4497)
- Commit: `refactor: extract signatures into MainWindow.Signatures.cs (no behavior change)`

### Task 14: `MainWindow.CanvasInteraction.cs`
- Sections: `Canvas interaction` (~5107)
- Commit: `refactor: extract canvas interaction into MainWindow.CanvasInteraction.cs (no behavior change)`

### Task 15: `MainWindow.Selection.cs`
- Sections: `Selection` (~5681)
- Commit: `refactor: extract selection into MainWindow.Selection.cs (no behavior change)`

### Task 16: `MainWindow.Search.cs`
- Sections: `Search (Ctrl+F)` (~6011)
- Commit: `refactor: extract search into MainWindow.Search.cs (no behavior change)`

### Task 17: `MainWindow.InlineTextEdit.cs`
- Sections: `Inline text editing (double-click)` (~6320)
- Commit: `refactor: extract inline text editing into MainWindow.InlineTextEdit.cs (no behavior change)`

### Task 18: `MainWindow.TextBox.cs`
- Sections: `Text box handling` (~6772)
- Commit: `refactor: extract text box handling into MainWindow.TextBox.cs (no behavior change)`

### Task 19: `MainWindow.KeyboardShortcuts.cs`
- Sections: `Keyboard shortcuts` (~6902)
- Commit: `refactor: extract keyboard shortcuts into MainWindow.KeyboardShortcuts.cs (no behavior change)`

### Task 20: `MainWindow.AnnotationManagement.cs`
- Sections: `Annotation management` (~7052)
- Commit: `refactor: extract annotation management into MainWindow.AnnotationManagement.cs (no behavior change)`

### Task 21: `MainWindow.DirtyTracking.cs`
- Sections: `Dirty / unsaved-change tracking` (~7330) **and** `Close file (Ctrl+W) — returns to drop-zone state` (~7345)
- Commit: `refactor: extract dirty tracking + close-file into MainWindow.DirtyTracking.cs (no behavior change)`

### Task 22: `MainWindow.FileToolbar.cs`
- Sections: `File toolbar handlers` (~7400)
- Commit: `refactor: extract file toolbar handlers into MainWindow.FileToolbar.cs (no behavior change)`

### Task 23: `MainWindow.SaveAnnotations.cs`
- Sections: `Save annotations to PDF` (~8151)
- Commit: `refactor: extract save-annotations into MainWindow.SaveAnnotations.cs (no behavior change)`

### Task 24: `MainWindow.TempReload.cs`
- Sections: `Bitmap rotation helper` (~8357) **and** `Temp save/reload` (~8400)
- Commit: `refactor: extract bitmap-rotation + temp reload into MainWindow.TempReload.cs (no behavior change)`

### Task 25: `MainWindow.Zoom.cs`
- Sections: `Zoom` (~8505)
- Commit: `refactor: extract zoom into MainWindow.Zoom.cs (no behavior change)`

### Task 26: `MainWindow.DragDrop.cs`
- Sections: `Drag/drop: file open` (~8979) **and** `Drag/drop: page reorder` (~9001)
- Commit: `refactor: extract drag/drop into MainWindow.DragDrop.cs (no behavior change)`

### Task 27: `MainWindow.PageSelection.cs`
- Sections: `Page selection handler` (~9050)
- Commit: `refactor: extract page selection into MainWindow.PageSelection.cs (no behavior change)`

### Task 28: `MainWindow.ViewMode.cs`
- Sections: `View Mode` (~9316)
- Commit: `refactor: extract view mode into MainWindow.ViewMode.cs (no behavior change)`

### Task 29: `MainWindow.Dialogs.cs`
- Sections: `Themed dialog — replaces MessageBox for dark-UI consistency` (~9597) — this is the **final** section; its body runs to the class's closing `}`. Move the members but leave the core file's closing `}` `}` (class + namespace) in place.
- Commit: `refactor: extract themed dialogs into MainWindow.Dialogs.cs (no behavior change)`

---

## Task 30: Full verification & core slimming check

**Files:** verification only.

- [ ] **Step 1: Confirm the core file is slim**

```bash
wc -l MainWindow.xaml.cs
```
Expected: only the `using` block, namespace, class declaration, all `_`-fields, the constructor, and the `Maximize-respects-taskbar fix` section remain (a few hundred lines, down from 9,738). No other `// ====` section banners remain except `Maximize-respects-taskbar fix`:
```bash
grep -n "// ====" -A1 MainWindow.xaml.cs | grep "//"
```
Expected: only the maximize-fix banner.

- [ ] **Step 2: Full build**

Run: `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`
Expected: `Build succeeded.`, `0 Error(s)`, warning count ≤ Task 0 baseline.

- [ ] **Step 3: Full test suite**

Run: `~/.dotnet/dotnet.exe test`
Expected: same passing count as Task 0 baseline; zero failures.

- [ ] **Step 4: e2e / screenshot harness**

Run the harness; compare to the Task 0 baseline. Expected: matches (no visual diff).

- [ ] **Step 5: Manual smoke pass**

Launch the app and exercise: open a PDF, edit (text + draw + highlight), Ctrl+S save, print preview, run OCR (Tools), switch theme, switch locale (incl. an RTL locale). Expected: all behave exactly as before the split.

- [ ] **Step 6: Confirm pure-move across the whole refactor**

```bash
git diff 9adcedb..HEAD -- MainWindow.xaml.cs | grep -c "^-"   # lines removed from core
```
Expected: the removed lines reappear (character-identical) across the new `MainWindow.*.cs` files; spot-check a couple of methods to confirm bodies are unchanged.

---

## Task 31: Update CLAUDE.md architecture note

**Files:**
- Modify: `CLAUDE.md` (the "Architecture" section describing the monolith)

The current text says *"`MainWindow.xaml.cs` (~9200 lines, 440 KB) is the monolith. Nearly all editing… lives in this one `partial class MainWindow`."* That is now stale.

- [ ] **Step 1: Edit the description**

Replace the stale monolith sentence with an accurate one, e.g.: *"`MainWindow` is a `partial class` split across ~30 `MainWindow.<Area>.cs` files (Settings, WindowChrome, FileOps, Links, Forms, Crop, Signatures, Search, Zoom, ViewMode, Dialogs, …), with a slim core `MainWindow.xaml.cs` holding the fields, constructor, and window-init. When adding UI behavior, find or add the matching area file."* Keep the ribbon/`SetMode`/`SetTool` paragraph intact.

- [ ] **Step 2: Commit**

```bash
git commit -m "docs(claude): describe MainWindow partial-class split

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** every spec item is covered — the 29 target files match the spec's mapping table (Tasks 1–29); fields-stay-in-core and `MainWindow.xaml`-untouched are Global Constraints; the build+test+e2e+smoke gate is Task 0 (baseline) + Task 30 (final); the slim-core acceptance criterion is Task 30 Step 1; the stale-CLAUDE.md doc follow-up is Task 31.
- **No new interfaces:** all members remain in one partial class, so there are no `Consumes`/`Produces` contracts between tasks — intentionally omitted.
- **Placeholders:** none — every task names its exact file, exact banner-title anchors, exact build/commit commands.
