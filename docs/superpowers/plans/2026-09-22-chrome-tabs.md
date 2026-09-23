# Chrome-style Document Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open several PDFs at once in one Scalpel window, each in its own tab holding a live
document session (unsaved annotations, undo/redo, dirty state, form values, rotations, view
position), switched and closed like Chrome tabs.

**Architecture:** A `DocumentSession` object owns everything that belongs to one open document.
MainWindow keeps ONE visual tree and rebinds it to the active session. The ~22 per-document
fields on MainWindow become same-named forwarding properties (`private PdfDocument? _doc { get => _s.Doc; set => _s.Doc = value; }`),
so the ~755 existing call sites compile unchanged and switching a tab is `_s = next` plus a UI
rebind. Ordering, launch parsing and memory rules live in WPF-free `Services/` classes that
`Scalpel.Tests` links and tests.

**Tech Stack:** C# / WPF on .NET Framework 4.8, PdfSharpCore 1.3.67, Docnet (PDFium via
`PdfiumGate`), xUnit (`Scalpel.Tests`, source-linked).

**Spec:** `docs/superpowers/specs/2026-09-22-chrome-tabs-research.md` (field inventory A.1-A.6,
open/close/save paths B, upstream KillerPDF's tab design C, architecture decision D). Read
sections A and D before Task 4.

## Global Constraints

- Target stays `net48`; no new NuGet packages.
- `dotnet` is at `~/.dotnet/dotnet.exe`. Build: `~/.dotnet/dotnet.exe build`. Tests: `~/.dotnet/dotnet.exe test`.
- Baseline: `dotnet test` has **8 pre-existing failures** (BatesNumberingServiceTests x3, WatermarkServiceTests x2, OcrServiceTests x2, FeatureMatrixTests). They are not regressions. Run `git checkout -- docs/samples` after every test run (tests regenerate the sample PDFs).
- A build copy error on `pdfium.dll` (MSB3027/MSB3021) means a running Scalpel.exe holds the file: close the app and rebuild.
- Every PDFium/Docnet call goes through `PdfiumGate.Run(...)`. Never add a render path that bypasses it.
- Never read `doc.Outlines` unless `doc.Internals.Catalog.Elements.ContainsKey("/Outlines")`.
- Every new string key goes into **all 9** `Strings/*.xaml` files (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru); `LocaleParityTests` enforces it.
- User-facing text: no em dashes or en dashes (plain hyphen), no emojis.
- Every user-visible change gets an entry in `Services/Changelog.cs` (What's New).
- Defensive `try { } catch { }` around I/O and UI glue, matching the existing partials.
- Do NOT run the E2E harness (`Scalpel.E2E`) without asking the user first; it takes over the desktop.
- Do not create git branches. Commit only when the user asks; the commit steps below are the suggested points and messages.
- **Design rule:** Task 10 presents tab-strip mockups and STOPS for the user's choice. Do not start Task 11 until the user has picked one.

## Review Focus

1. **A long operation finishing after a tab switch** (compress, OCR, redact, straighten, form OCR): the result must land in the tab that started it, never in the tab that is active when it finishes. Pinned by Task 7's `LongOperationGate` tests and its manual check.
2. **Closing the window with several dirty tabs:** every dirty tab must be offered for saving; none may be dropped silently. Pinned by Task 6 step "window close" plus the `DirtySessionsInCloseOrder` test.
3. **Opening a file that is already open:** it must switch to the existing tab (clean or dirty), not open a second copy that can overwrite the first on save. Pinned by `TabListModel` `FindByPath` tests in Task 2.
4. **Queued dispatcher callbacks from the previous tab** (render, zoom restore, scroll restore) must not apply to the new tab (upstream #378/#379/#399). Pinned by Task 5's `SessionGeneration` guard and its manual rapid Ctrl+Tab check.
5. **Save As, then switch away and back:** the tab must show the new name and reopen the new path, not the old one. Pinned by Task 6's Save As step (identity is the session, not the path).

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `Services/TabListModel.cs` | Create | Pure ordered tab list: add, activate, remove-and-pick-next, move, cycle, jump-by-number, close-others, close-to-right, find-by-path. |
| `Services/DocumentPath.cs` | Create | Path identity: normalize + compare two paths (case-insensitive full path). |
| `Services/LongOperationGate.cs` | Create | Pure counter of in-flight long operations; tab switching is refused while it is non-zero. |
| `Services/UndoBudgetCoordinator.cs` | Create | Pure global undo byte budget across all tabs. |
| `Services/OpenTabsSetting.cs` | Create (Task 12) | Pure serialize/parse of the open-tab list for the registry. |
| `Services/SingleInstance.cs` | Modify | `PickLaunchTargets` returns every existing file, not only the first. |
| `MainWindow.DocumentSession.cs` | Create | Nested `DocumentSession` class plus the forwarding-property shim. |
| `MainWindow.Tabs.cs` | Rewrite | Session list, switch, close, busy gate, strip binding. Replaces the path-only switcher. |
| `MainWindow.xaml.cs` | Modify | Remove the per-document field declarations that move into the session. |
| `MainWindow.FileOps.cs` | Modify | `OpenFile` builds into a new session; `FinishOpenFile` split into reset + bind; thumbnails per session. |
| `MainWindow.DirtyTracking.cs` | Modify | `MarkDirty` refreshes the tab chip; `CloseFile` closes the active tab. |
| `MainWindow.WindowChrome.cs` | Modify | `OnClosing` walks every dirty session. |
| `MainWindow.FileToolbar.cs`, `MainWindow.DragDrop.cs`, `MainWindow.Recent.cs`, `MainWindow.SingleInstance.cs` | Modify | Multi-open entry points. |
| `MainWindow.Tools.cs`, `MainWindow.FormOcr.cs`, `MainWindow.Straighten.cs` | Modify | Pin the session at the start of each long operation. |
| `MainWindow.KeyboardShortcuts.cs` | Modify | Tab shortcuts routed to the session model. |
| `MainWindow.xaml` | Modify (Task 11) | Tab strip template after the mockup choice. |
| `App.xaml.cs` | Modify | Per-owner temp files, `ReleaseTempFiles(owner)`. |
| `Strings/*.xaml` (9 files) | Modify | New tab strings. |
| `Services/Changelog.cs` | Modify | What's New entry. |
| `Scalpel.Tests/*Tests.cs` + `Scalpel.Tests.csproj` | Create/Modify | Tests for every new Services class, linked via `<Compile Include>`. |

---

### Task 1: Groundwork fixes that tabs would amplify

These are live bugs today (found in the research, spec section B). Fixing them first keeps each later task's diff about tabs only.

**Files:**
- Modify: `MainWindow.FileOps.cs` (`FinishOpenFile`, ~line 627)
- Modify: `MainWindow.DirtyTracking.cs` (`CloseFile`, ~line 62-100)
- Modify: `MainWindow.Settings.cs` (`SaveWindowSettings`, ~line 386-389)
- Modify: `MainWindow.FileToolbar.cs` (`Open_Click` ~line 73), `MainWindow.DragDrop.cs` (`DropZone_Drop` ~line 35), `MainWindow.Recent.cs` (`OpenRecent` ~line 41)
- Modify: `Services/Changelog.cs`

**Interfaces:**
- Produces: `private bool ConfirmDiscardIfDirty()` in `MainWindow.DirtyTracking.cs` - returns `true` when there is nothing unsaved or the user agreed to discard. Task 6 deletes its callers again (every open then goes to a new tab), so keep it small.

- [ ] **Step 1: Clear redo history with undo history**

In `FinishOpenFile`, next to the existing `_undoStack.Clear();`, add:

```csharp
_redoStack.Clear();
```

In `CloseFile`, in the block that clears the model after `_doc.Close()`, add the same line plus:

```csharp
_redoStack.Clear();
_pageRotations.Clear();
_openedProtected = false;
```

- [ ] **Step 2: Persist the user's file, not the working copy, as LastFile**

In `SaveWindowSettings`, the `LastFile` write uses `_currentFile`. Change it to prefer the user's path:

```csharp
string? last = _originalFile ?? _currentFile;
```

and use `last` wherever `_currentFile` was used for `LastFile` (including the `IsTransientPath` check). Leave the rest of the method alone.

- [ ] **Step 3: Add the interim discard guard**

Add to `MainWindow.DirtyTracking.cs`:

```csharp
/// <summary>
/// Interim guard for entry points that replace the open document. Returns true when nothing
/// is unsaved or the user agreed to discard. Removed again once every open gets its own tab.
/// </summary>
private bool ConfirmDiscardIfDirty()
{
    if (!_isDirty || _doc is null) return true;
    var res = ScalpelDialog.Show(this, Loc("Str_Dlg_UnsavedClose"), "Scalpel",
        MessageBoxButton.YesNo, MessageBoxImage.Warning);
    return res == MessageBoxResult.Yes;
}
```

Call it as the first statement that runs before `OpenFile(...)` in `Open_Click`, `DropZone_Drop` and `OpenRecent`:

```csharp
if (!ConfirmDiscardIfDirty()) return;
```

- [ ] **Step 4: Build and run the unit tests**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`
Expected: build succeeds; exactly the 8 known failures.

- [ ] **Step 5: Manual check**

Launch `bin/Debug/net48/Scalpel.exe`. Open a PDF, add a text annotation, then drag another PDF onto the window. Expected: the "unsaved changes" prompt appears; answering No keeps the annotated document open.

- [ ] **Step 6: What's New entry and commit**

Prepend to the newest `Release` in `Services/Changelog.cs`: "Opening another file no longer discards unsaved changes without asking." Commit:

```bash
git add MainWindow.FileOps.cs MainWindow.DirtyTracking.cs MainWindow.Settings.cs MainWindow.FileToolbar.cs MainWindow.DragDrop.cs MainWindow.Recent.cs Services/Changelog.cs
git commit -m "fix: prompt before replacing an unsaved document; clear redo on open/close; LastFile stores the real path"
```

---

### Task 2: `TabListModel` and `DocumentPath` (pure, tested)

**Files:**
- Create: `Services/TabListModel.cs`
- Create: `Services/DocumentPath.cs`
- Create: `Scalpel.Tests/TabListModelTests.cs`
- Modify: `Scalpel.Tests/Scalpel.Tests.csproj` (two `<Compile Include>` links)

**Interfaces:**
- Produces: `Scalpel.Services.TabListModel<T> where T : class` with `Items`, `Active`, `Count`, `IndexOf(T)`, `Add(T item, bool activate = true)`, `Activate(T)`, `Remove(T) -> T?` (the tab that becomes active), `Move(int from, int to)`, `Cycle(bool forward) -> T?`, `ByNumber(int n) -> T?`, `OthersThan(T) -> IReadOnlyList<T>`, `RightOf(T) -> IReadOnlyList<T>`, `FindByPath(string path, Func<T, string?> pathOf) -> T?`.
- Produces: `Scalpel.Services.DocumentPath.Same(string? a, string? b) -> bool`.

- [ ] **Step 1: Write the failing tests**

`Scalpel.Tests/TabListModelTests.cs`:

```csharp
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TabListModelTests
    {
        private sealed class Tab { public string? Path; public Tab(string? p) { Path = p; } }

        private static (TabListModel<Tab> m, Tab a, Tab b, Tab c) Three()
        {
            var m = new TabListModel<Tab>();
            Tab a = new(@"C:\a.pdf"), b = new(@"C:\b.pdf"), c = new(@"C:\c.pdf");
            m.Add(a); m.Add(b); m.Add(c);
            return (m, a, b, c);
        }

        [Fact]
        public void Add_appends_and_activates()
        {
            var (m, _, _, c) = Three();
            Assert.Equal(3, m.Count);
            Assert.Same(c, m.Active);
        }

        [Fact]
        public void Add_without_activate_keeps_the_active_tab()
        {
            var (m, _, _, c) = Three();
            var d = new Tab(@"C:\d.pdf");
            m.Add(d, activate: false);
            Assert.Same(c, m.Active);
            Assert.Same(d, m.Items.Last());
        }

        [Fact]
        public void Removing_the_active_tab_activates_the_right_neighbour()
        {
            var (m, a, b, c) = Three();
            m.Activate(b);
            Assert.Same(c, m.Remove(b));
            Assert.Same(c, m.Active);
        }

        [Fact]
        public void Removing_the_last_active_tab_activates_the_left_neighbour()
        {
            var (m, _, b, c) = Three();
            Assert.Same(b, m.Remove(c));
        }

        [Fact]
        public void Removing_a_background_tab_keeps_the_active_tab()
        {
            var (m, a, _, c) = Three();
            Assert.Same(c, m.Remove(a));
        }

        [Fact]
        public void Removing_the_only_tab_leaves_no_active_tab()
        {
            var m = new TabListModel<Tab>();
            var a = new Tab(null);
            m.Add(a);
            Assert.Null(m.Remove(a));
            Assert.Equal(0, m.Count);
        }

        [Fact]
        public void Removing_twice_is_a_harmless_no_op()   // upstream #353
        {
            var (m, a, _, c) = Three();
            m.Remove(a);
            Assert.Same(c, m.Remove(a));
            Assert.Equal(2, m.Count);
        }

        [Fact]
        public void Cycle_wraps_in_both_directions()
        {
            var (m, a, _, c) = Three();
            Assert.Same(a, m.Cycle(forward: true));
            m.Activate(a);
            Assert.Same(c, m.Cycle(forward: false));
        }

        [Fact]
        public void Cycle_does_not_change_the_active_tab()
        {
            var (m, _, _, c) = Three();
            m.Cycle(forward: true);
            Assert.Same(c, m.Active);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(9, 2)]    // 9 = last tab, like Chrome
        public void ByNumber_maps_digits_to_tabs(int n, int expectedIndex)
        {
            var (m, _, _, _) = Three();
            Assert.Same(m.Items[expectedIndex], m.ByNumber(n));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(10)]
        public void ByNumber_out_of_range_is_null(int n)
        {
            var (m, _, _, _) = Three();
            Assert.Null(m.ByNumber(n));
        }

        [Fact]
        public void Move_reorders_and_clamps()
        {
            var (m, a, b, c) = Three();
            m.Move(0, 99);
            Assert.Equal(new[] { b, c, a }, m.Items);
        }

        [Fact]
        public void OthersThan_and_RightOf_return_snapshots()
        {
            var (m, a, b, c) = Three();
            Assert.Equal(new[] { a, c }, m.OthersThan(b));
            Assert.Equal(new[] { c }, m.RightOf(b));
            Assert.Empty(m.RightOf(c));
        }

        [Fact]
        public void FindByPath_ignores_case_and_relative_segments()
        {
            var (m, _, b, _) = Three();
            Assert.Same(b, m.FindByPath(@"c:\X\..\B.PDF", t => t.Path));
            Assert.Null(m.FindByPath(@"C:\zzz.pdf", t => t.Path));
        }

        [Fact]
        public void DocumentPath_Same_handles_nulls()
        {
            Assert.False(DocumentPath.Same(null, @"C:\a.pdf"));
            Assert.False(DocumentPath.Same(null, null));
            Assert.True(DocumentPath.Same(@"C:\A.pdf", @"c:\a.PDF"));
        }
    }
}
```

Add to `Scalpel.Tests/Scalpel.Tests.csproj`, next to the other `Services` links:

```xml
<Compile Include="..\Services\TabListModel.cs" Link="Services\TabListModel.cs" />
<Compile Include="..\Services\DocumentPath.cs" Link="Services\DocumentPath.cs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TabListModel"`
Expected: build error, `TabListModel` / `DocumentPath` not found.

- [ ] **Step 3: Implement**

`Services/DocumentPath.cs`:

```csharp
using System;
using System.IO;

namespace Scalpel.Services
{
    /// <summary>Identity of a document on disk: two spellings of the same file compare equal.</summary>
    public static class DocumentPath
    {
        public static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        public static bool Same(string? a, string? b)
        {
            var na = Normalize(a);
            var nb = Normalize(b);
            return na is not null && nb is not null
                && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

`Services/TabListModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// The ordered list of open document tabs and which one is active. WPF-free so the ordering
    /// rules (who becomes active after a close, cycling, number jumps) are unit-tested. Identity
    /// is the tab object, never its path, so Save As and untitled documents need no special case.
    /// </summary>
    public sealed class TabListModel<T> where T : class
    {
        private readonly List<T> _items = [];

        public IReadOnlyList<T> Items => _items;
        public T? Active { get; private set; }
        public int Count => _items.Count;

        public int IndexOf(T item) => _items.FindIndex(i => ReferenceEquals(i, item));

        public void Add(T item, bool activate = true)
        {
            if (item is null) throw new ArgumentNullException(nameof(item));
            if (IndexOf(item) < 0) _items.Add(item);
            if (activate || Active is null) Active = item;
        }

        public bool Activate(T item)
        {
            if (IndexOf(item) < 0) return false;
            Active = item;
            return true;
        }

        /// <summary>
        /// Removes <paramref name="item"/> and returns the tab that is active afterwards: the right
        /// neighbour of a closed active tab, else its left neighbour, else null. Removing a tab that
        /// is not in the list (a stale or repeated close request) changes nothing.
        /// </summary>
        public T? Remove(T item)
        {
            int idx = IndexOf(item);
            if (idx < 0) return Active;
            _items.RemoveAt(idx);
            if (!ReferenceEquals(Active, item)) return Active;
            Active = _items.Count == 0 ? null : _items[Math.Min(idx, _items.Count - 1)];
            return Active;
        }

        public void Move(int from, int to)
        {
            if (from < 0 || from >= _items.Count) return;
            to = Math.Max(0, Math.Min(_items.Count - 1, to));
            if (from == to) return;
            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);
        }

        /// <summary>The tab Ctrl+Tab (forward) or Ctrl+Shift+Tab would go to. Does not activate it.</summary>
        public T? Cycle(bool forward)
        {
            if (_items.Count == 0) return null;
            int idx = Active is null ? 0 : Math.Max(0, IndexOf(Active));
            int next = forward ? (idx + 1) % _items.Count : (idx - 1 + _items.Count) % _items.Count;
            return _items[next];
        }

        /// <summary>1..8 pick that tab; 9 picks the last tab (Chrome's rule). Anything else is null.</summary>
        public T? ByNumber(int n)
        {
            if (n < 1 || n > 9 || _items.Count == 0) return null;
            if (n == 9) return _items[_items.Count - 1];
            return n - 1 < _items.Count ? _items[n - 1] : null;
        }

        public IReadOnlyList<T> OthersThan(T keep) =>
            _items.Where(i => !ReferenceEquals(i, keep)).ToList();

        public IReadOnlyList<T> RightOf(T item)
        {
            int idx = IndexOf(item);
            return idx < 0 ? [] : _items.Skip(idx + 1).ToList();
        }

        public T? FindByPath(string path, Func<T, string?> pathOf) =>
            _items.FirstOrDefault(i => DocumentPath.Same(pathOf(i), path));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TabListModel"; git checkout -- docs/samples`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Services/TabListModel.cs Services/DocumentPath.cs Scalpel.Tests/TabListModelTests.cs Scalpel.Tests/Scalpel.Tests.csproj
git commit -m "feat(tabs): pure tab-list model with close/cycle/jump rules"
```

---

### Task 3: Launch parsing returns every file

**Files:**
- Modify: `Services/SingleInstance.cs` (`SingleInstanceProtocol`)
- Modify: `Scalpel.Tests/SingleInstanceProtocolTests.cs`

**Interfaces:**
- Produces: `SingleInstanceProtocol.PickLaunchTargets(IEnumerable<string> args, Func<string,bool> fileExists) -> (IReadOnlyList<string> files, bool edit)`. `PickLaunchTarget` stays (returns the first file) so current callers keep compiling until Task 8.

- [ ] **Step 1: Write the failing tests** (append to `SingleInstanceProtocolTests`)

```csharp
[Fact]
public void PickLaunchTargets_returns_every_existing_file_in_order()
{
    var existing = new[] { @"C:\a.pdf", @"C:\b.pdf" };
    var (files, edit) = SingleInstanceProtocol.PickLaunchTargets(
        [@"C:\a.pdf", "/edit", @"C:\missing.pdf", @"C:\b.pdf"],
        p => Array.IndexOf(existing, p) >= 0);
    Assert.Equal(existing, files);
    Assert.True(edit);
}

[Fact]
public void PickLaunchTargets_drops_duplicates_of_the_same_file()
{
    var (files, _) = SingleInstanceProtocol.PickLaunchTargets(
        [@"C:\a.pdf", @"c:\A.PDF"], _ => true);
    Assert.Single(files);
}

[Fact]
public void PickLaunchTargets_survives_a_throwing_existence_check()
{
    var (files, _) = SingleInstanceProtocol.PickLaunchTargets(
        [@"C:\a.pdf"], _ => throw new System.IO.IOException());
    Assert.Empty(files);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~SingleInstanceProtocol"`
Expected: build error, `PickLaunchTargets` not found.

- [ ] **Step 3: Implement** (inside `SingleInstanceProtocol`)

```csharp
/// <summary>
/// Every argument that is an existing file, in order and without duplicates, plus whether
/// <c>/edit</c> appears anywhere. Each file opens as its own tab.
/// </summary>
public static (IReadOnlyList<string> files, bool edit) PickLaunchTargets(
    IEnumerable<string> args, Func<string, bool> fileExists)
{
    var files = new List<string>();
    bool edit = false;
    foreach (var a in args)
    {
        if (string.Equals(a, "/edit", StringComparison.OrdinalIgnoreCase)) { edit = true; continue; }
        bool exists = false;
        try { exists = fileExists(a); } catch { }
        if (exists && !files.Exists(f => DocumentPath.Same(f, a))) files.Add(a);
    }
    return (files, edit);
}
```

Rewrite the body of `PickLaunchTarget` to reuse it:

```csharp
var (files, edit) = PickLaunchTargets(args, fileExists);
return (files.Count > 0 ? files[0] : null, edit);
```

`DocumentPath` must be linked in the test project already (Task 2). The existing `PickLaunchTarget` tests must stay green.

- [ ] **Step 4: Run to verify pass**

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~SingleInstanceProtocol"; git checkout -- docs/samples`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add Services/SingleInstance.cs Scalpel.Tests/SingleInstanceProtocolTests.cs
git commit -m "feat(tabs): launch parsing returns every file argument"
```

---

### Task 4: `DocumentSession` and the forwarding shim (still one document)

No visible change. After this task every per-document field lives in one object, which is what makes Tasks 5-6 safe.

**Files:**
- Create: `MainWindow.DocumentSession.cs`
- Modify: `MainWindow.xaml.cs` (field declarations at lines ~24-27, 31, 37, 41, 51, 55, 75-80, 107-109, 130-131, 263, 266-268)
- Modify: `MainWindow.Forms.cs` (declaration of `_formAppearanceFailed`, ~line 615)
- Modify: `MainWindow.TextBox.cs` (declaration of `_blankCache`, ~line 31-32)
- Modify: `CLAUDE.md` (architecture note)

**Interfaces:**
- Produces: nested `private sealed class DocumentSession` inside `MainWindow`, and the active-session field `private DocumentSession _s`. Property names (used by Tasks 5-12): `Id`, `Doc`, `WorkingPath`, `OriginalPath`, `OpenedProtected`, `HasFormFields`, `FormAppearanceFailed`, `IsDirty`, `Annotations`, `RenderDims`, `PageRotations`, `FormText`, `FormCheck`, `FormRadio`, `Undo`, `Redo`, `BlankCache`, `SearchRects`, `SearchPages`, `SearchCursor`, `ZoomLevel`, `FitMode`, `ViewMode`, `PageIndex`, `ScrollH`, `ScrollV`, `SearchQuery`, `DisplayName`, `LastActivated`, and `static DocumentSession CreateLike(DocumentSession? template)`.

- [ ] **Step 1: Create the session class and the shim**

`MainWindow.DocumentSession.cs`:

```csharp
using System;
using System.Collections.Generic;
using PdfSharpCore.Pdf;
using Scalpel.Models;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Document session: everything that belongs to ONE open document (one tab).
        //
        // The fields below USED to be MainWindow fields. They are now same-named properties that
        // forward to the ACTIVE session `_s`, so existing code (`_doc`, `_annotations`, `_isDirty`,
        // ...) keeps compiling and always talks to the tab being shown. Switching tabs is `_s = next`
        // plus a UI rebind; nothing is copied, so nothing can be forgotten.
        //
        // Async code that must keep writing to the document it started on captures `var s = _s;`
        // and uses `s.X` after an await, never the forwarding property.
        // ============================================================

        private sealed class DocumentSession
        {
            public Guid Id { get; } = Guid.NewGuid();

            // Model
            public PdfDocument? Doc;
            public string? WorkingPath;        // was _currentFile: temp/working copy PDFium renders
            public string? OriginalPath;       // was _originalFile: the user's file, Save target
            public bool OpenedProtected;
            public bool HasFormFields;
            public bool FormAppearanceFailed;
            public bool IsDirty;
            public readonly Dictionary<int, List<PageAnnotation>> Annotations = [];
            public readonly Dictionary<int, (int w, int h)> RenderDims = [];
            public readonly Dictionary<int, int> PageRotations = [];
            public readonly Dictionary<int, string> FormText = [];
            public readonly Dictionary<int, bool> FormCheck = [];
            public readonly Dictionary<string, string> FormRadio = [];
            public readonly Stack<UndoEntry> Undo = new();
            public readonly Stack<UndoEntry> Redo = new();
            public Dictionary<int, List<Scalpel.Services.TextEntryPlaceholder.Candidate>> BlankCache = [];
            public readonly Dictionary<int, List<(double left, double bottom, double right, double top)>> SearchRects = [];
            public readonly List<int> SearchPages = [];
            public int SearchCursor = -1;

            // View (restored when the tab is shown again)
            public double ZoomLevel = 1.0;
            public FitMode FitMode = FitMode.None;
            public ViewMode ViewMode = ViewMode.Continuous;
            public int PageIndex;
            public double ScrollH, ScrollV;
            public string? SearchQuery;
            public DateTime LastActivated = DateTime.UtcNow;

            public string DisplayName =>
                string.IsNullOrEmpty(OriginalPath) ? "Untitled.pdf" : System.IO.Path.GetFileName(OriginalPath);

            /// <summary>A fresh session that inherits the view preferences of <paramref name="template"/>.</summary>
            public static DocumentSession CreateLike(DocumentSession? template) => new()
            {
                ZoomLevel = template?.ZoomLevel ?? 1.0,
                FitMode = template?.FitMode ?? FitMode.None,
                ViewMode = template?.ViewMode ?? ViewMode.Continuous,
            };
        }

        /// <summary>The session shown in the window. Never null.</summary>
        private DocumentSession _s = new();

        // ---- forwarding shim (one line per former field) ----
        private PdfDocument? _doc { get => _s.Doc; set => _s.Doc = value; }
        private string? _currentFile { get => _s.WorkingPath; set => _s.WorkingPath = value; }
        private string? _originalFile { get => _s.OriginalPath; set => _s.OriginalPath = value; }
        private bool _openedProtected { get => _s.OpenedProtected; set => _s.OpenedProtected = value; }
        private bool _docHasFormFields { get => _s.HasFormFields; set => _s.HasFormFields = value; }
        private bool _formAppearanceFailed { get => _s.FormAppearanceFailed; set => _s.FormAppearanceFailed = value; }
        private bool _isDirty { get => _s.IsDirty; set => _s.IsDirty = value; }
        private Dictionary<int, List<PageAnnotation>> _annotations => _s.Annotations;
        private Dictionary<int, (int w, int h)> _renderDims => _s.RenderDims;
        private Dictionary<int, int> _pageRotations => _s.PageRotations;
        private Dictionary<int, string> _formTextValues => _s.FormText;
        private Dictionary<int, bool> _formCheckValues => _s.FormCheck;
        private Dictionary<string, string> _formRadioValues => _s.FormRadio;
        private Stack<UndoEntry> _undoStack => _s.Undo;
        private Stack<UndoEntry> _redoStack => _s.Redo;
        private Dictionary<int, List<Scalpel.Services.TextEntryPlaceholder.Candidate>> _blankCache
        { get => _s.BlankCache; set => _s.BlankCache = value; }
        private Dictionary<int, List<(double left, double bottom, double right, double top)>> _allSearchRects => _s.SearchRects;
        private List<int> _searchResultPages => _s.SearchPages;
        private int _searchPageCursor { get => _s.SearchCursor; set => _s.SearchCursor = value; }
        private double _zoomLevel { get => _s.ZoomLevel; set => _s.ZoomLevel = value; }
        private FitMode _fitMode { get => _s.FitMode; set => _s.FitMode = value; }
        private ViewMode _viewMode { get => _s.ViewMode; set => _s.ViewMode = value; }
    }
}
```

Check the `using` lines against what `MainWindow.xaml.cs` imports (`PageAnnotation` lives in `Models/EditingTypes.cs`; copy its namespace from there).

- [ ] **Step 2: Delete the old declarations**

Remove from `MainWindow.xaml.cs` the declarations of every name in the shim above (keep the comments that explain them by moving those comments onto the matching `DocumentSession` members). Keep `FitMode`, `ViewMode`, `UndoKind`, `UndoEntry` type declarations where they are; the nested class can see them. Initial values move to the session defaults: `_zoomLevel = 1.0`, `_fitMode = FitMode.None`, `_viewMode = ViewMode.Continuous`, `_searchPageCursor = -1`, `_isDirty = false`.

Remove `_formAppearanceFailed` from `MainWindow.Forms.cs` and `_blankCache` from `MainWindow.TextBox.cs` (keep `InvalidateBlankCache`).

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet.exe build`
Expected: success. If a call site fails with CS0206 (`ref`/`out` on a property) or CS0191, rewrite that one line to use a local; the research found none, so any hit is new code.

- [ ] **Step 4: Run the unit tests**

Run: `~/.dotnet/dotnet.exe test; git checkout -- docs/samples`
Expected: exactly the 8 known failures.

- [ ] **Step 5: Manual regression check**

Open a PDF, annotate, undo, redo, rotate a page, fill a form field, search, change view mode and zoom, save, reopen. Expected: identical behaviour to before.

- [ ] **Step 6: Document and commit**

Add to `CLAUDE.md` under Architecture, after the MainWindow partials paragraph:

```markdown
- **Per-document state lives in `DocumentSession` (`MainWindow.DocumentSession.cs`).** `_doc`, `_currentFile`, `_originalFile`, `_annotations`, `_undoStack`, `_isDirty`, zoom/view mode and the rest are forwarding properties to the active session `_s`, not fields. A new per-document value goes on `DocumentSession` plus a shim line; never add it as a MainWindow field or it will leak across tabs. Async work that must finish in its own document captures `var s = _s;` before the first await.
```

```bash
git add MainWindow.DocumentSession.cs MainWindow.xaml.cs MainWindow.Forms.cs MainWindow.TextBox.cs CLAUDE.md
git commit -m "refactor(tabs): move per-document state into DocumentSession behind a forwarding shim"
```

---

### Task 5: Split `FinishOpenFile`; per-session thumbnails; stale-callback guard

Still one visible document. This task creates the three primitives a tab switch is built from.

**Files:**
- Modify: `MainWindow.FileOps.cs` (`FinishOpenFile` ~618-679, `RefreshPageList` ~807-841, `_thumbCts` ~805)
- Modify: `MainWindow.Tabs.cs` (new primitives; the old path list stays until Task 6)
- Modify: `MainWindow.Tools.cs` (`AdoptTransformedFile` ~53-60)
- Modify: `MainWindow.DocumentSession.cs` (thumbnail fields)
- Modify: the deferred callbacks listed in spec D.5 item 2 (`MainWindow.FileOps.cs:957`, `MainWindow.ViewMode.cs:52/60/204/319`, `MainWindow.Zoom.cs:75/116/182`, `MainWindow.TempReload.cs:157/167`)

**Interfaces:**
- Consumes: `DocumentSession` (Task 4).
- Produces (in `MainWindow.Tabs.cs`):
  - `private void ResetSessionContent(DocumentSession s)` - clears model collections for new content (what the reset half of `FinishOpenFile` did), without touching UI.
  - `private void BindSessionToUi(DocumentSession s)` - labels, dirty colour, thumbnails, outline, view mode, explicit render of `s.PageIndex`, then scroll restore.
  - `private void DeactivateSession()` - commits the active textbox, cancels transient UI and all render work, captures view state into `_s`.
  - `private int _sessionGeneration` plus `private bool IsStale(int generation) => generation != _sessionGeneration;`
- Produces on `DocumentSession`: `public PageThumbnailVm[]? Thumbs; public string? ThumbsForPath; public System.Threading.CancellationTokenSource? ThumbCts;`

- [ ] **Step 1: Move the thumbnail cancellation and cache into the session**

Add the three thumbnail members to `DocumentSession`. Replace the `_thumbCts` field in `MainWindow.FileOps.cs` with a shim line in `MainWindow.DocumentSession.cs`:

```csharp
private System.Threading.CancellationTokenSource? _thumbCts { get => _s.ThumbCts; set => _s.ThumbCts = value; }
```

In `RefreshPageList`, the block that seeds new `PageThumbnailVm`s with `oldItems[i].Thumbnail` must only do so when the old thumbnails came from the same working file:

```csharp
bool sameSource = DocumentPath.Same(_s.ThumbsForPath, _currentFile);
// ... existing loop: only copy oldItems[i].Thumbnail when sameSource is true
```

At the end of `RefreshPageList` record them:

```csharp
_s.Thumbs = items;              // the array just assigned to PageList.ItemsSource
_s.ThumbsForPath = _currentFile;
```

This fixes the "previous document's thumbnails in the sidebar" defect.

- [ ] **Step 2: Add the generation guard**

In `MainWindow.Tabs.cs`:

```csharp
/// <summary>
/// Bumped on every tab switch and every open. A dispatcher callback queued for an earlier
/// generation belongs to a document that is no longer shown and must do nothing
/// (upstream #378/#379/#399).
/// </summary>
private int _sessionGeneration;
private bool IsStale(int generation) => generation != _sessionGeneration;
```

In each deferred callback listed under Files, capture `int gen = _sessionGeneration;` before `Dispatcher.BeginInvoke` and make the first line inside the callback `if (IsStale(gen)) return;`.

- [ ] **Step 3: Split `FinishOpenFile`**

Move the model-reset statements (clearing `_annotations`, `_undoStack`, `_redoStack`, `_renderDims`, blank cache, `_pageRotations`, form dictionaries, search state, `_openedProtected`, recomputing `_docHasFormFields`) into:

```csharp
private void ResetSessionContent(DocumentSession s)
{
    s.Annotations.Clear();
    s.Undo.Clear();
    s.Redo.Clear();
    s.RenderDims.Clear();
    s.BlankCache = [];
    s.PageRotations.Clear();
    s.FormText.Clear();
    s.FormCheck.Clear();
    s.FormRadio.Clear();
    s.SearchRects.Clear();
    s.SearchPages.Clear();
    s.SearchCursor = -1;
    s.OpenedProtected = false;
    s.IsDirty = false;
    s.PageIndex = 0;
    s.ScrollH = s.ScrollV = 0;
    s.Thumbs = null;
    s.ThumbsForPath = null;
}
```

Move the UI statements (header label, `ClearSecondaryPages`, `ClearSelection`, `RefreshPageList`, `LoadOutlines`, preview visibility, `MarkDirty`, page selection, continuous bootstrap, queued fit, status) into `BindSessionToUi(DocumentSession s)`. In it, render the primary page explicitly instead of relying on `PageList.SelectedIndex = 0` firing `SelectionChanged` (upstream #378):

```csharp
_sessionGeneration++;
int gen = _sessionGeneration;
// ... existing label / list / outline / view-mode setup ...
PageList.SelectedIndex = Math.Min(s.PageIndex, Math.Max(0, (_doc?.PageCount ?? 1) - 1));
if (_viewMode != ViewMode.Continuous)
    RenderPage(_viewMode == ViewMode.Grid ? 0 : PageList.SelectedIndex);
Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, (Action)(() =>
{
    if (IsStale(gen)) return;
    try
    {
        PagePreviewPanel.ScrollToHorizontalOffset(s.ScrollH);
        PagePreviewPanel.ScrollToVerticalOffset(s.ScrollV);
    }
    catch { }
}));
```

`FinishOpenFile` becomes: recent-list update, set paths, `ResetSessionContent(_s)`, recompute `_docHasFormFields`, `BindSessionToUi(_s)`, log, `AddTab(_originalFile)` (still the old path strip until Task 6).

- [ ] **Step 4: `DeactivateSession`**

```csharp
private void DeactivateSession()
{
    try { CommitActiveTextBox(); } catch { }
    try { ClearSelection(); ClearTextSelection(); } catch { }
    try { HideDrawSettings(); HideTextSettings(); HideSignaturePopup(); } catch { }
    try { _thumbCts?.Cancel(); } catch { }
    try { _secondaryRenderCts?.Cancel(); } catch { }
    try { _continuousRenderCts?.Cancel(); } catch { }
    try { _rerenderTimer?.Stop(); } catch { }
    try
    {
        _s.PageIndex = Math.Max(0, PageList.SelectedIndex);
        _s.ScrollH = PagePreviewPanel.HorizontalOffset;   // snapshot BEFORE any rebuild (upstream #399)
        _s.ScrollV = PagePreviewPanel.VerticalOffset;
        _s.SearchQuery = _searchBox?.Text;
    }
    catch { }
    _sessionGeneration++;
}
```

Check the exact names of the cancel helpers against `CloseFile` (`MainWindow.DirtyTracking.cs:80-83`), which already hides the same popups, and against the crop/measure cancel methods in `MainWindow.Crop.cs` / `MainWindow.Measure.cs`; call those too.

- [ ] **Step 5: `AdoptTransformedFile` must not add a tab**

In `MainWindow.Tools.cs`, `AdoptTransformedFile` currently calls `FinishOpenFile`. Change it to replace content in the same session:

```csharp
_currentFile = workingPath;
ResetSessionContent(_s);
_docHasFormFields = /* same expression FinishOpenFile uses */;
BindSessionToUi(_s);
MarkDirty(true);
```

- [ ] **Step 6: Build, test, manual check**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`
Expected: 8 known failures only.

Manual: open file A, open file B via the existing path strip, switch back to A. Expected: sidebar shows A's thumbnails immediately (never B's). In Grid view, switch between two files: the first tile is the right document's page 1.

- [ ] **Step 7: Commit**

```bash
git add MainWindow.FileOps.cs MainWindow.Tabs.cs MainWindow.Tools.cs MainWindow.DocumentSession.cs MainWindow.ViewMode.cs MainWindow.Zoom.cs MainWindow.TempReload.cs
git commit -m "refactor(tabs): split open into reset/bind, per-session thumbnails, stale-callback guard"
```

---

### Task 6: Live multi-session tabs

The behaviour change. After this task each tab keeps its own live document.

**Files:**
- Rewrite: `MainWindow.Tabs.cs` (replace `_openTabs`, `_tabState`, `RememberTabState`, `RestoreTabState`, `AddTab`, `SwitchToTab`, `CloseTab`, `CloseOtherTabs`, `CycleTab`)
- Modify: `MainWindow.FileOps.cs` (`OpenFile` and every branch that does `_doc.Close()` first: lines ~71, 100, 118, 145, 168, 200, 496, 601)
- Modify: `MainWindow.DirtyTracking.cs` (`MarkDirty`, `CloseFile`, delete `ConfirmDiscardIfDirty` and its three callers from Task 1)
- Modify: `MainWindow.WindowChrome.cs` (`OnClosing`)
- Modify: `MainWindow.FileToolbar.cs` (`SaveAs_Click` ~559/567, `SaveInPlace` post-save reload ~453, `NewDocument` ~42-71)
- Modify: `MainWindow.KeyboardShortcuts.cs` (lines ~169-191)
- Modify: `MainWindow.xaml.cs` (`Closed` handler: close every session's `Doc`)
- Modify: `Strings/*.xaml` (9 files), `Services/Changelog.cs`
- Create: `Scalpel.Tests/TabCloseOrderTests.cs`

**Interfaces:**
- Consumes: `TabListModel<DocumentSession>` (Task 2), `DocumentPath` (Task 2), `ResetSessionContent` / `BindSessionToUi` / `DeactivateSession` / `_sessionGeneration` (Task 5).
- Produces (used by Tasks 7, 8, 11, 12):
  - `private readonly TabListModel<DocumentSession> _tabs = new();`
  - `private void SwitchTo(DocumentSession target)`
  - `private bool CloseSession(DocumentSession s)` - returns false when the user cancelled.
  - `private void OpenInTab(string path)` - dedups by path, else opens into a new session.
  - `private void RefreshTabStrip()` (kept name; now draws from `_tabs`)
  - `internal static IReadOnlyList<T> DirtySessionsInCloseOrder<T>(IReadOnlyList<T> tabs, T? active, Func<T,bool> isDirty)` in `Services/TabListModel.cs` as a static helper class `TabCloseOrder` (active tab first, then left to right).

- [ ] **Step 1: Test the close order helper**

`Scalpel.Tests/TabCloseOrderTests.cs`:

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TabCloseOrderTests
    {
        [Fact]
        public void Active_dirty_tab_is_asked_about_first_then_left_to_right()
        {
            string[] tabs = ["a", "b", "c", "d"];
            var dirty = new[] { "a", "c", "d" };
            var order = TabCloseOrder.DirtyFirstActive(tabs, "c", t => System.Array.IndexOf(dirty, t) >= 0);
            Assert.Equal(new[] { "c", "a", "d" }, order);
        }

        [Fact]
        public void Clean_tabs_are_never_listed()
        {
            string[] tabs = ["a", "b"];
            Assert.Empty(TabCloseOrder.DirtyFirstActive(tabs, "a", _ => false));
        }
    }
}
```

Add to `Services/TabListModel.cs`:

```csharp
public static class TabCloseOrder
{
    /// <summary>Dirty tabs in the order the window-close flow asks about them: the tab being
    /// looked at first, then the rest left to right.</summary>
    public static IReadOnlyList<T> DirtyFirstActive<T>(IReadOnlyList<T> tabs, T? active, Func<T, bool> isDirty)
        where T : class
    {
        var result = new List<T>();
        if (active is not null && isDirty(active)) result.Add(active);
        foreach (var t in tabs)
            if (!ReferenceEquals(t, active) && isDirty(t)) result.Add(t);
        return result;
    }
}
```

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~TabCloseOrder"` - expected FAIL before the helper exists, PASS after.

- [ ] **Step 2: New strings (all 9 locale files)**

Add to every `Strings/*.xaml` (translate for each; the English is the source):

```xml
<sys:String x:Key="Str_Tab_SavePrompt">Save changes to {0} before closing?</sys:String>
<sys:String x:Key="Str_Tab_AlreadyOpen">{0} is already open.</sys:String>
<sys:String x:Key="Str_Tab_Busy">Finish or cancel the current operation before switching documents.</sys:String>
```

Run: `~/.dotnet/dotnet.exe test --filter "FullyQualifiedName~LocaleParity"` - expected PASS.

- [ ] **Step 3: Session list, switch and open**

Replace the path list in `MainWindow.Tabs.cs` with `private readonly TabListModel<DocumentSession> _tabs = new();`. The first session: in the constructor, after `_s` exists, call `_tabs.Add(_s)`.

```csharp
private void SwitchTo(DocumentSession target)
{
    try
    {
        if (ReferenceEquals(target, _s)) return;
        if (_tabs.IndexOf(target) < 0) return;
        if (_longOps.IsBusy) { ShowToast(Loc("Str_Tab_Busy")); return; }   // Task 7 adds _longOps; until then omit this line
        DeactivateSession();
        _s = target;
        _tabs.Activate(target);
        target.LastActivated = DateTime.UtcNow;
        BindSessionToUi(target);
        RefreshTabStrip();
    }
    catch { }
}
```

Open: split `OpenFile(path)` so the fallback chain runs against a fresh session and the previous session is never closed:

```csharp
private void OpenInTab(string path)
{
    var existing = _tabs.FindByPath(path, t => t.OriginalPath);
    if (existing is not null)
    {
        SwitchTo(existing);
        ShowToast(string.Format(Loc("Str_Tab_AlreadyOpen"), System.IO.Path.GetFileName(path)));
        return;
    }

    var previous = _s;
    bool reusePlaceholder = previous.Doc is null && !previous.IsDirty;   // empty start state
    var fresh = reusePlaceholder ? previous : DocumentSession.CreateLike(previous);
    if (!reusePlaceholder)
    {
        DeactivateSession();
        _s = fresh;
        _tabs.Add(fresh);
    }

    OpenFile(path);                       // existing fallback chain, now writes into `fresh`

    if (fresh.Doc is null && !reusePlaceholder)
    {
        // Every fallback failed: drop the empty tab and go back (upstream AbortTabLoad).
        _tabs.Remove(fresh);
        _s = previous;
        _tabs.Activate(previous);
        BindSessionToUi(previous);
    }
    RefreshTabStrip();
}
```

In `OpenFile`, delete each "`if (_doc is not null) { _doc.Close(); _doc = null; }`" at the start of a fallback branch: `_doc` is now the fresh session's, which is null. Keep the closes that dispose a failed attempt's own document inside the same branch. Remove the `AddTab(...)` call from `FinishOpenFile`.

Route every caller that means "open a document" to `OpenInTab`: `Open_Click`, `DropZone_Drop`, `OpenRecent`, the command line in the `Loaded` handler, `HandleForwardedLaunch`. Delete `ConfirmDiscardIfDirty` and its three calls from Task 1.

`NewDocument`: create the blank document in a new session the same way (reuse the placeholder when empty); `OriginalPath` stays null so `DisplayName` is "Untitled.pdf", and `SaveInPlace` must route to Save As when `OriginalPath` is null.

- [ ] **Step 4: Close with Save / Don't save / Cancel**

```csharp
private bool _closingTab;

/// <returns>false when the user cancelled.</returns>
private bool CloseSession(DocumentSession s)
{
    if (_closingTab) return false;                          // upstream #353: repeated request
    if (_tabs.IndexOf(s) < 0) return true;                  // already gone
    _closingTab = true;
    try
    {
        if (s.IsDirty)
        {
            SwitchTo(s);                                    // show the document being asked about
            var res = ScalpelDialog.Show(this,
                string.Format(Loc("Str_Tab_SavePrompt"), s.DisplayName),
                "Scalpel", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Cancel || res == MessageBoxResult.None) return false;
            if (res == MessageBoxResult.Yes)
            {
                SaveInPlace();
                if (s.IsDirty) return false;                // save failed or Save As cancelled
            }
            if (_tabs.IndexOf(s) < 0) return true;          // re-check after the modal
        }

        bool wasActive = ReferenceEquals(s, _s);
        if (wasActive) DeactivateSession();
        try { s.ThumbCts?.Cancel(); } catch { }
        try { s.Doc?.Close(); } catch { }
        s.Doc = null;
        App.ReleaseTempFiles(s.Id);                          // Task 9 adds this; until then omit
        var next = _tabs.Remove(s);

        if (next is null)
        {
            _s = DocumentSession.CreateLike(s);
            _tabs.Add(_s);
            ShowEmptyState();                                // the UI half of today's CloseFile
        }
        else if (wasActive)
        {
            _s = next;
            BindSessionToUi(next);
        }
        RefreshTabStrip();
        return true;
    }
    finally { _closingTab = false; }
}
```

Extract the UI-reset half of today's `CloseFile` (drop zone visible, clear canvases, labels, outline, `PageList.ItemsSource = null`) into `ShowEmptyState()`. `CloseFile()` becomes `CloseSession(_s);`.

Close others / to the right: iterate a snapshot and stop at the first cancel:

```csharp
private void CloseMany(IReadOnlyList<DocumentSession> victims)
{
    foreach (var v in victims) if (!CloseSession(v)) break;
}
```

`CloseOtherTabs()` = `CloseMany(_tabs.OthersThan(_s))`; `CloseTabsToRight()` = `CloseMany(_tabs.RightOf(_s))`.

- [ ] **Step 5: Window close walks every dirty tab**

Replace the body of `OnClosing`:

```csharp
protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
{
    try { DeactivateSession(); } catch { }                   // capture the active tab first
    var dirty = TabCloseOrder.DirtyFirstActive(_tabs.Items, _s, t => t.IsDirty);
    foreach (var s in dirty)
    {
        SwitchTo(s);
        var res = ScalpelDialog.Show(this,
            string.Format(Loc("Str_Tab_SavePrompt"), s.DisplayName),
            Loc("Str_Dlg_AppTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Cancel || res == MessageBoxResult.None) { e.Cancel = true; return; }
        if (res == MessageBoxResult.Yes)
        {
            SaveInPlace();
            if (s.IsDirty) { e.Cancel = true; return; }
        }
    }
    SaveWindowSettings();
    base.OnClosing(e);
}
```

In the `Closed` handler (`MainWindow.xaml.cs` ~312), close every session's document: `foreach (var t in _tabs.Items) { try { t.Doc?.Close(); } catch { } }`.

- [ ] **Step 6: Live dirty state, Save As, post-save reload**

`MarkDirty` (`MainWindow.DirtyTracking.cs:29`): after setting `_isDirty` and recolouring `SaveAsBtn`, call `RefreshTabStrip();` so the active tab's dirty marker is live (upstream's lags; do not copy that).

`SaveAs_Click`: it already assigns `_originalFile`, which now writes into the session; add `RefreshTabStrip();` after it so the tab shows the new name. Save As onto a path that another tab has open: before writing, if `_tabs.FindByPath(newPath, t => t.OriginalPath)` returns a different session, show `Str_Tab_AlreadyOpen` and abort.

`SaveInPlace` post-save reload failure (~line 453) calls `OpenFile(saveTarget)`: keep it (it now reloads into the same session because `_s` is still that session); do not route it through `OpenInTab`.

- [ ] **Step 7: Rewire shortcuts**

In `MainWindow.KeyboardShortcuts.cs`: Ctrl+W -> `CloseSession(_s)`; Ctrl+Shift+W -> `CloseOtherTabs()`; Ctrl+Tab / Ctrl+Shift+Tab -> `var t = _tabs.Cycle(forward); if (t is not null) SwitchTo(t);` guarded by `_tabs.Count > 1`; add Ctrl+PageDown / Ctrl+PageUp as the same two actions. Ctrl+T and number jumps come in Task 11.

- [ ] **Step 8: Keep the existing chip UI, bound to sessions**

`RefreshTabStrip` iterates `_tabs.Items` instead of `_openTabs`; a chip's label is `s.DisplayName` with a leading dot when `s.IsDirty` (placeholder marker until Task 11), tooltip `s.OriginalPath ?? s.DisplayName`, click -> `SwitchTo(s)`, close button -> `CloseSession(s)`. Show the strip when `_tabs.Count > 1` (unchanged rule until the user decides in Task 10).

- [ ] **Step 9: Build, test, manual scenarios**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`
Expected: 8 known failures only.

Manual (all must hold):
1. Open A, add a text annotation (A dirty). Open B. Switch to A: the annotation is there, Undo removes it, Redo restores it.
2. Close A's tab while dirty: prompt names A; Cancel keeps it; Don't save closes it and B becomes active.
3. Two dirty tabs, close the window: asked about the active one first, then the other; Cancel on either keeps the app open.
4. Save As in tab B to a new name: the tab label changes; switch away and back: it shows the new file.
5. Open A again while it is open: switches to A with the "already open" toast.
6. Open a file that fails every fallback while A is open: A stays open and active, no empty tab left behind.

- [ ] **Step 10: What's New and commit**

`Services/Changelog.cs`: "Each open PDF now keeps its own tab with its unsaved changes, undo history and place in the document. Closing a tab or Scalpel asks about every unsaved document."

```bash
git add MainWindow.*.cs Services/TabListModel.cs Scalpel.Tests/TabCloseOrderTests.cs Scalpel.Tests/Scalpel.Tests.csproj Strings/*.xaml Services/Changelog.cs
git commit -m "feat(tabs): live document session per tab with per-tab save prompts"
```

---

### Task 7: Long operations stay with their document

**Files:**
- Create: `Services/LongOperationGate.cs`, `Scalpel.Tests/LongOperationGateTests.cs`
- Modify: `MainWindow.Tabs.cs`, `MainWindow.Tools.cs` (compress ~641-683, OCR ~722-750, redact ~1151-1157), `MainWindow.FormOcr.cs` (~64-83), `MainWindow.Straighten.cs` (~103), `MainWindow.FileToolbar.cs` (flatten ~581-713, print ~782+), `MainWindow.ExportImages.cs` (~89), `MainWindow.Compare.cs` (~57)

**Interfaces:**
- Produces: `Scalpel.Services.LongOperationGate` with `IDisposable Begin()`, `bool IsBusy`.
- Produces: `private readonly LongOperationGate _longOps = new();` on MainWindow, and `AdoptTransformedFile(DocumentSession target, string workingPath)` overload.

- [ ] **Step 1: Failing tests**

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LongOperationGateTests
    {
        [Fact]
        public void Busy_while_any_operation_is_open()
        {
            var g = new LongOperationGate();
            Assert.False(g.IsBusy);
            var a = g.Begin();
            var b = g.Begin();
            a.Dispose();
            Assert.True(g.IsBusy);
            b.Dispose();
            Assert.False(g.IsBusy);
        }

        [Fact]
        public void Disposing_a_token_twice_does_not_go_negative()
        {
            var g = new LongOperationGate();
            var a = g.Begin();
            a.Dispose();
            a.Dispose();
            var b = g.Begin();
            Assert.True(g.IsBusy);
            b.Dispose();
        }
    }
}
```

Link `..\Services\LongOperationGate.cs` in `Scalpel.Tests.csproj`. Run the filter; expected FAIL.

- [ ] **Step 2: Implement**

```csharp
using System;
using System.Threading;

namespace Scalpel.Services
{
    /// <summary>Counts operations that finish after an await (compress, OCR, flatten, ...).
    /// Tab switching and closing are refused while any is running, so a result can never land
    /// in a document other than the one it started on.</summary>
    public sealed class LongOperationGate
    {
        private int _count;
        public bool IsBusy => Volatile.Read(ref _count) > 0;

        public IDisposable Begin()
        {
            Interlocked.Increment(ref _count);
            return new Token(this);
        }

        private sealed class Token(LongOperationGate owner) : IDisposable
        {
            private int _done;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref owner._count);
            }
        }
    }
}
```

Run the filter; expected PASS.

- [ ] **Step 3: Gate and pin each long operation**

Pattern for every handler in Files (shown for compress):

```csharp
var session = _s;                          // pin before the first await
using var op = _longOps.Begin();
// ... existing work, unchanged, but every write AFTER an await uses `session`:
AdoptTransformedFile(session, resultPath);
```

`AdoptTransformedFile(DocumentSession target, string workingPath)`: set `target.WorkingPath`, `ResetSessionContent(target)`, `target.IsDirty = true`; call `BindSessionToUi(target)` only when `ReferenceEquals(target, _s)`, then `RefreshTabStrip()`. Form OCR writes `session.FormText[...]` and `session.IsDirty = true` instead of the shim properties.

`SwitchTo`, `CloseSession`, `OpenInTab` and the tab keyboard shortcuts return early with the `Str_Tab_Busy` toast while `_longOps.IsBusy`; `OnClosing` cancels the close with the same toast.

- [ ] **Step 4: Build, test, manual check**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`. Expected: 8 known failures only.

Manual: open two files, start Tools > Compress on A, press Ctrl+Tab during it. Expected: the busy toast; after it finishes, A shows the compressed result and B is untouched.

- [ ] **Step 5: Commit**

```bash
git add Services/LongOperationGate.cs Scalpel.Tests/LongOperationGateTests.cs Scalpel.Tests/Scalpel.Tests.csproj MainWindow.*.cs
git commit -m "fix(tabs): long operations finish in the document that started them"
```

---

### Task 8: Open several files at once

**Files:**
- Modify: `MainWindow.FileToolbar.cs` (`Open_Click` ~73-77), `MainWindow.DragDrop.cs` (`DropZone_Drop` ~35-43), `MainWindow.xaml.cs` (`Loaded` command line ~328-370), `MainWindow.SingleInstance.cs` (`HandleForwardedLaunch` ~18-49)
- Modify: `Strings/*.xaml` (9 files), `Services/Changelog.cs`

**Interfaces:**
- Consumes: `OpenInTab` (Task 6), `SingleInstanceProtocol.PickLaunchTargets` (Task 3).
- Produces: `private void OpenManyInTabs(IEnumerable<string> paths)`.

- [ ] **Step 1: `OpenManyInTabs`**

```csharp
private const int ManyFilesConfirmThreshold = 20;

private void OpenManyInTabs(IEnumerable<string> paths)
{
    var list = paths.Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
    if (list.Count == 0) return;
    if (list.Count > ManyFilesConfirmThreshold)
    {
        var res = ScalpelDialog.Show(this, string.Format(Loc("Str_Tab_OpenMany"), list.Count),
            "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
    }
    foreach (var p in list) OpenInTab(p);
    var first = _tabs.FindByPath(list[0], t => t.OriginalPath);
    if (first is not null) SwitchTo(first);        // land on the first file, like Explorer's order
}
```

Add `Str_Tab_OpenMany` ("Open {0} files as tabs?") to all 9 locale files.

- [ ] **Step 2: Wire the four entry points**

- `Open_Click`: `Multiselect = true`, then `OpenManyInTabs(dlg.FileNames)`.
- `DropZone_Drop`: `OpenManyInTabs(files)` instead of `files[0]`.
- Command line in `Loaded`: `var (files, edit) = SingleInstanceProtocol.PickLaunchTargets(args, File.Exists); if (files.Count > 0) OpenManyInTabs(files); else /* existing LastFile fallback */;`
- `HandleForwardedLaunch`: same with the forwarded args; if the window has not finished `Loaded`, store the list in a `List<string> _pendingForwarded` and flush it at the end of the `Loaded` handler.

- [ ] **Step 3: Build, test, manual check**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`. Expected: 8 known failures only.

Manual: select 3 PDFs in the Open dialog -> 3 tabs. Drag 3 PDFs onto an open document -> 3 more tabs, the open one untouched. With Scalpel running, select 2 PDFs in Explorer and press Enter -> both open as tabs in the running window.

- [ ] **Step 4: What's New and commit**

`Services/Changelog.cs`: "Open several PDFs at once from the Open dialog, by dragging them in, or from Explorer; each gets its own tab."

```bash
git add MainWindow.*.cs Strings/*.xaml Services/Changelog.cs
git commit -m "feat(tabs): open several files at once from dialog, drop, command line and Explorer"
```

---

### Task 9: Memory and temp files per tab

**Files:**
- Create: `Services/UndoBudgetCoordinator.cs`, `Scalpel.Tests/UndoBudgetCoordinatorTests.cs`
- Modify: `App.xaml.cs` (`MakeTempFile` ~822, add `ReleaseTempFiles`), every `App.MakeTempFile(` caller in `MainWindow.*.cs` (pass the owner), `MainWindow.Tabs.cs`, `MainWindow.AnnotationManagement.cs` (`PushDocUndo` ~45-52)

**Interfaces:**
- Produces: `UndoBudgetCoordinator.TrimAcross<T>(IReadOnlyList<Stack<T>> stacksLeastRecentFirst, Func<T,long> sizeOf, long maxTotalBytes) -> long` (remaining total).
- Produces: `App.MakeTempFile(string tag, Guid owner)` overload and `App.ReleaseTempFiles(Guid owner)`.

- [ ] **Step 1: Failing tests**

```csharp
using System.Collections.Generic;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class UndoBudgetCoordinatorTests
    {
        private static Stack<long> S(params long[] oldestFirst)
        {
            var s = new Stack<long>();
            foreach (var x in oldestFirst) s.Push(x);
            return s;
        }

        [Fact]
        public void Under_budget_nothing_is_trimmed()
        {
            var a = S(10, 10); var b = S(10);
            Assert.Equal(30, UndoBudgetCoordinator.TrimAcross([a, b], x => x, 100));
            Assert.Equal(2, a.Count);
        }

        [Fact]
        public void Oldest_entries_of_the_least_recent_tab_go_first()
        {
            var old = S(50, 40);      // least recently used tab
            var active = S(30);
            long left = UndoBudgetCoordinator.TrimAcross([old, active], x => x, 80);
            Assert.Equal(70, left);
            Assert.Equal(new long[] { 40 }, old.ToArray());   // the 50 (oldest) was dropped
            Assert.Single(active);
        }

        [Fact]
        public void The_newest_entry_of_the_active_tab_is_always_kept()
        {
            var active = S(10, 500);
            UndoBudgetCoordinator.TrimAcross([active], x => x, 100);
            Assert.Equal(new long[] { 500 }, active.ToArray());
        }
    }
}
```

Link `..\Services\UndoBudgetCoordinator.cs`. Run the filter; expected FAIL.

- [ ] **Step 2: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>
    /// One memory budget for the undo history of every open tab. Each document undo holds a
    /// whole copy of the PDF, so ten tabs at the per-tab cap would pin gigabytes. Trims oldest
    /// entries from the least recently used tab first; the newest entry of the last stack (the
    /// active tab) is always kept.
    /// </summary>
    public static class UndoBudgetCoordinator
    {
        public static long TrimAcross<T>(IReadOnlyList<Stack<T>> stacksLeastRecentFirst,
            Func<T, long> sizeOf, long maxTotalBytes)
        {
            long total = stacksLeastRecentFirst.Sum(s => s.Sum(e => Math.Max(0, sizeOf(e))));
            for (int i = 0; i < stacksLeastRecentFirst.Count && total > maxTotalBytes; i++)
            {
                var stack = stacksLeastRecentFirst[i];
                bool keepNewest = i == stacksLeastRecentFirst.Count - 1;
                var newestFirst = stack.ToList();
                while (total > maxTotalBytes && newestFirst.Count > (keepNewest ? 1 : 0))
                {
                    total -= Math.Max(0, sizeOf(newestFirst[newestFirst.Count - 1]));
                    newestFirst.RemoveAt(newestFirst.Count - 1);
                }
                stack.Clear();
                for (int k = newestFirst.Count - 1; k >= 0; k--) stack.Push(newestFirst[k]);
            }
            return total;
        }
    }
}
```

Run the filter; expected PASS.

- [ ] **Step 3: Apply the budget**

After every document-undo push (`PushDocUndo`), call:

```csharp
private const long UndoMaxBytesAllTabs = 512L * 1024 * 1024;

private void TrimUndoAcrossTabs()
{
    var byAge = _tabs.Items.OrderBy(t => ReferenceEquals(t, _s) ? DateTime.MaxValue : t.LastActivated).ToList();
    var stacks = byAge.SelectMany(t => new[] { t.Redo, t.Undo }).ToList();
    UndoBudgetCoordinator.TrimAcross(stacks, UndoEntrySize, UndoMaxBytesAllTabs);
}
```

`UndoEntrySize` is the existing size function next to `UndoMaxBytes` in `MainWindow.xaml.cs`; reuse it.

- [ ] **Step 4: Per-tab temp files**

In `App.xaml.cs`, keep `MakeTempFile(string tag)` and add an owner-aware overload plus release:

```csharp
private static readonly Dictionary<Guid, List<string>> _ownedTemps = [];

internal static string MakeTempFile(string tag, Guid owner)
{
    var path = MakeTempFile(tag);
    lock (_sessionTemps)
    {
        if (!_ownedTemps.TryGetValue(owner, out var list)) _ownedTemps[owner] = list = [];
        list.Add(path);
    }
    return path;
}

/// <summary>Deletes the temp files a closed tab created. The exit sweep stays as the backstop.</summary>
internal static void ReleaseTempFiles(Guid owner)
{
    List<string>? list;
    lock (_sessionTemps)
    {
        if (!_ownedTemps.TryGetValue(owner, out list)) return;
        _ownedTemps.Remove(owner);
        foreach (var f in list) _sessionTemps.Remove(f);
    }
    foreach (var f in list) { try { File.Delete(f); } catch { } }
}
```

Change each `App.MakeTempFile(tag)` call in `MainWindow.*.cs` that creates a document's working, decrypted, repaired, undo or burn copy to `App.MakeTempFile(tag, _s.Id)` (for a pinned async operation: `session.Id`). Add the `App.ReleaseTempFiles(s.Id)` line in `CloseSession` (Task 6 step 4).

- [ ] **Step 5: Drop bitmaps of the tab being left, compact after close**

In `DeactivateSession`, after cancelling render work: `PageImage.Source = null; ClearSecondaryPages();` and clear `_continuousPanel.Children`. After `CloseSession` removes a tab, schedule one LOH compaction:

```csharp
Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, (Action)(() =>
{
    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect();
}));
```

Keep `Thumbs` for the 4 most recently activated background tabs; set `Thumbs = null` on older ones inside `SwitchTo`.

- [ ] **Step 6: Build, test, manual check, commit**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`. Expected: 8 known failures only.

Manual: open 3 files, rotate pages in each (creates temps), close one tab; `%LOCALAPPDATA%\Scalpel\Temp` loses that tab's files immediately and keeps the others'.

```bash
git add Services/UndoBudgetCoordinator.cs Scalpel.Tests/UndoBudgetCoordinatorTests.cs Scalpel.Tests/Scalpel.Tests.csproj App.xaml.cs MainWindow.*.cs
git commit -m "perf(tabs): shared undo budget, per-tab temp cleanup, drop background bitmaps"
```

---

### Task 10: Tab strip mockups - STOP for the user's choice

**Files:**
- Create: `design-mockups/tabs/tab-strip-options.html` (static, uses the Light + Red Clinical tokens from `Themes/Light.xaml` / `Themes/Accents/Light_Red.xaml` and the Dark tokens)

- [x] **Step 1: Build three options on one page**, each shown in Light and Dark, with 1, 4 and 14 tabs (overflow), one dirty tab, one hovered close button:
  - **A - Chrome tabs above the ribbon:** trapezoid-free rounded tabs in the title bar row beside the Quick Access Toolbar, "+" button at the end.
  - **B - Document tab row under the ribbon** (where today's chips sit): flat tabs with an accent top bar on the active one, "+" at the end, overflow chevron listing all tabs.
  - **C - Compact chips** (evolved current look): pill chips with dirty dot and close on hover, horizontal scroll plus chevron.
- [x] **Step 2: Ask the user** to pick A, B or C. **Chosen: C - compact chips** (2026-09-22). Task 11 builds option C exactly as drawn in the mockup file (`.optC` styles: 26 px pill chips on a `RibbonBand` row under the ribbon, active = `AccentDim` fill + `AccentBorder` + `AccentText`, dirty dot in the close-button slot, close shown on hover and on the active chip, + button, chevron list when more than 6 tabs, horizontal scroll).

  Decided by the user on 2026-09-22 (do not ask again):
  1. The strip shows even with a single tab (Chrome style).
  2. Ctrl+1..9 go to tabs; the zoom presets move: Ctrl+0 actual size (unchanged), Ctrl+Shift+2 fit width, Ctrl+Shift+3 fit page (proposed in the mockup page; confirm with the A/B/C pick).
  3. Restore the previous session's open tabs at startup: yes (Task 12 is in scope).

  Mockup file: `design-mockups/tabs/tab-strip-options.html` (live: option, 1/4/14 files, Light/Dark).
- [x] **Step 3: Wait.** Done: the user chose C.

---

### Task 11: Tab strip implementation (after the choice)

**Files:**
- Modify: `MainWindow.xaml` (replace `TabStripHost` / `TabStrip` at ~886-891), `Themes/_Shared.xaml` (a `DocumentTab` style), `MainWindow.Tabs.cs`, `MainWindow.KeyboardShortcuts.cs`, `MainWindow.PageSelection.cs` (F1 shortcut overlay ~121), `Strings/*.xaml` (9 files), `docs/UI-REFERENCE.md`, `Services/Changelog.cs`

**Interfaces:**
- Consumes: `_tabs`, `SwitchTo`, `CloseSession`, `CloseOtherTabs`, `CloseTabsToRight`, `NewDocument` (Tasks 6-8).

The visual details follow the chosen mockup. The behaviour is fixed:

- [ ] **Step 1: Chip behaviour.** Left-click switches on mouse UP (so a press can start a drag); middle-click closes; close button closes; dirty marker bound to `IsDirty` and refreshed from `MarkDirty`; tooltip is the full path; label ellipsis at 180 px.
- [ ] **Step 2: Reorder by drag.** Start after `SystemParameters.MinimumHorizontalDragDistance`; call `_tabs.Move(from, to)` when the dragged tab's centre crosses a neighbour's midpoint; `RefreshTabStrip()`.
- [ ] **Step 3: Context menu** (themed, `MakeMenuItem` like `MainWindow.ContextMenu.cs`): Close (Ctrl+W), Close other tabs (Ctrl+Shift+W, disabled with 1 tab), Close tabs to the right (disabled on the last tab), separator, Open containing folder (disabled when `OriginalPath` is null or missing; runs `explorer.exe /select,"<path>"`), Copy path. New keys: `Str_Tab_Close`, `Str_Tab_CloseOthers`, `Str_Tab_CloseRight`, `Str_Tab_OpenFolder`, `Str_Tab_CopyPath`, `Str_Tab_New` in all 9 locale files; run `LocaleParityTests`.
- [ ] **Step 4: Overflow.** Horizontal scroll inside the existing ScrollViewer, active tab scrolled into view on switch, plus a chevron menu listing every tab (double `_` in headers so underscores are not access keys).
- [ ] **Step 5: Shortcuts.** Ctrl+T = `NewDocument()` in a new tab; Ctrl+1..9 jump via `_tabs.ByNumber(n)`; move the zoom presets in `MainWindow.KeyboardShortcuts.cs` (~227-238) to Ctrl+0 actual size, Ctrl+Shift+2 fit width, Ctrl+Shift+3 fit page, and update `ZoomShortcutLabel` / tooltips. Add every tab shortcut to the F1 overlay and `docs/UI-REFERENCE.md`.
- [ ] **Step 6: Build, test, manual check.** `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples` (8 known failures). Check in Light, Dark and High Contrast, and in Hebrew (the strip mirrors under RTL).
- [ ] **Step 7: What's New and commit**: "New tab bar: drag tabs to reorder, middle-click to close, right-click for Close others and Open containing folder."

```bash
git add MainWindow.xaml Themes/_Shared.xaml MainWindow.*.cs Strings/*.xaml docs/UI-REFERENCE.md Services/Changelog.cs
git commit -m "feat(tabs): tab bar with reorder, context menu, overflow and shortcuts"
```

---

### Task 12: Restore open tabs at startup (approved by the user 2026-09-22)

**Files:**
- Create: `Services/OpenTabsSetting.cs`, `Scalpel.Tests/OpenTabsSettingTests.cs`
- Modify: `MainWindow.Settings.cs` (`SaveWindowSettings`), `MainWindow.xaml.cs` (`Loaded`), `MainWindow.DocumentSession.cs` (`DeferredPath`), `MainWindow.Tabs.cs`

**Interfaces:**
- Produces: `OpenTabsSetting.Serialize(IReadOnlyList<string> paths, int activeIndex) -> string` and `OpenTabsSetting.Parse(string? value, Func<string,bool> keep) -> (List<string> paths, int activeIndex)`.

- [ ] **Step 1: Failing tests**

```csharp
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class OpenTabsSettingTests
    {
        [Fact]
        public void Round_trips_paths_and_active_index()
        {
            string wire = OpenTabsSetting.Serialize([@"C:\a.pdf", @"D:\b c.pdf"], 1);
            var (paths, active) = OpenTabsSetting.Parse(wire, _ => true);
            Assert.Equal(new[] { @"C:\a.pdf", @"D:\b c.pdf" }, paths);
            Assert.Equal(1, active);
        }

        [Fact]
        public void Dropped_paths_shift_the_active_index()
        {
            string wire = OpenTabsSetting.Serialize([@"C:\gone.pdf", @"C:\a.pdf", @"C:\b.pdf"], 2);
            var (paths, active) = OpenTabsSetting.Parse(wire, p => p != @"C:\gone.pdf");
            Assert.Equal(new[] { @"C:\a.pdf", @"C:\b.pdf" }, paths);
            Assert.Equal(1, active);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        public void Bad_values_yield_nothing(string? wire)
        {
            var (paths, active) = OpenTabsSetting.Parse(wire, _ => true);
            Assert.Empty(paths);
            Assert.Equal(-1, active);
        }
    }
}
```

- [ ] **Step 2: Implement** (Windows paths cannot contain `|`)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scalpel.Services
{
    /// <summary>Registry value for the tabs open at exit: "activeIndex|path|path|...".</summary>
    public static class OpenTabsSetting
    {
        public static string Serialize(IReadOnlyList<string> paths, int activeIndex) =>
            string.Join("|", new[] { activeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(paths));

        public static (List<string> paths, int activeIndex) Parse(string? value, Func<string, bool> keep)
        {
            var none = (new List<string>(), -1);
            if (string.IsNullOrEmpty(value)) return none;
            var parts = value!.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int active)) return none;

            var kept = new List<string>();
            int newActive = -1;
            for (int i = 1; i < parts.Length; i++)
            {
                bool ok = false;
                try { ok = parts[i].Length > 0 && keep(parts[i]); } catch { }
                if (!ok) continue;
                if (i - 1 == active) newActive = kept.Count;
                kept.Add(parts[i]);
            }
            if (kept.Count == 0) return none;
            return (kept, newActive < 0 ? 0 : newActive);
        }
    }
}
```

Link it in the test csproj; run the filter; expected PASS.

- [ ] **Step 3: Save and restore**

`SaveWindowSettings`: write `OpenTabs` = `OpenTabsSetting.Serialize(realPaths, activeIndex)` where `realPaths` are the tabs' `OriginalPath`s that are non-null and not transient (`IsTransientPath`). Keep writing `LastFile` for older builds.

Startup (`Loaded`, only when no file was passed on the command line): parse `OpenTabs` with `File.Exists`; create one session per path with `DeferredPath = path` (no document loaded); materialize only the active one through `OpenInTab`. In `SwitchTo`, when the target has `DeferredPath` set and `Doc` is null, open it into that session before binding.

- [ ] **Step 4: Build, test, manual check, commit**

Run: `~/.dotnet/dotnet.exe build && ~/.dotnet/dotnet.exe test; git checkout -- docs/samples`. Manual: open 3 files, activate the second, quit, relaunch: 3 tabs, the second active, the others load on first click. What's New: "Scalpel reopens the tabs you had open."

```bash
git add Services/OpenTabsSetting.cs Scalpel.Tests/OpenTabsSettingTests.cs Scalpel.Tests/Scalpel.Tests.csproj MainWindow.*.cs Services/Changelog.cs
git commit -m "feat(tabs): restore open tabs at startup"
```

---

### Task 13: Docs and E2E coverage

**Files:**
- Modify: `docs/UI-REFERENCE.md`, `docs/OVERVIEW.md`, `CLAUDE.md` (tabs paragraph under App.xaml.cs single-instance), `Scalpel.E2E/Catalog/Catalog.cs`, `Scalpel.E2E/Suites/*` (new scenarios)

- [ ] **Step 1: Docs.** Describe the session model, the shortcuts, and the close/save rules. In CLAUDE.md replace "opens the file as a tab" with "opens each file as its own tab (live `DocumentSession`)".
- [ ] **Step 2: E2E scenarios** (write them; do not run): open two files, annotate A, switch to B and back (annotation and undo survive); dirty marker; close a dirty tab (Cancel / Don't save); window close with two dirty tabs; drop three files; Ctrl+Tab; Save As renames the tab; a long operation blocks switching.
- [ ] **Step 3: Ask the user** whether to run `Scalpel.E2E --suite all --sequential` now. Only run it after a yes. Expected baseline: 245/246 passing (the 1 is the known text-extraction glyph collapse).
- [ ] **Step 4: Commit**

```bash
git add docs/UI-REFERENCE.md docs/OVERVIEW.md CLAUDE.md Scalpel.E2E
git commit -m "docs(tabs): document tabs; add E2E scenarios"
```
