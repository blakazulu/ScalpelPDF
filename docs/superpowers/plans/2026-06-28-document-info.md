# Document Info (F12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A themed form (Tools menu + F12) to view/edit the PDF's Title/Author/Subject/Keywords/Creator metadata plus a read-only summary, persisted on the next save.

**Architecture:** Reuse the existing `ShowToolForm` themed modal — no new dialog UI. A `ShowDocumentInfo()` handler reads `_doc.Info.*`, shows the form, writes edits back, and marks dirty; `_doc.Save` (existing) persists them.

**Tech Stack:** C# / .NET Framework 4.8, WPF, PdfSharpCore. Build via `~/.dotnet/dotnet.exe`.

## Global Constraints
- Build via `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug`; tests via `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj` (baseline 187 must stay green).
- pdfium lock gotcha: close any running `Scalpel.exe` before building.
- All edited files stay UTF-8-BOM + CRLF.
- Every new `Str_*` key must be added to ALL 9 `Strings/*.xaml` (en-US, es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru) or it blanks/falls back in that language. Keep brand tokens Latin.
- Do not change `MetadataSanitizer` (separate remove-only feature).
- Reuse `ShowToolForm`/`ToolField`/`RequireOpenDoc`/`MarkDirty`/`SetStatus`/`ScalpelDialog` — do not reinvent.

---

## Task 1: Localization keys (all 9 locales) + changelog

After this task the strings exist (additive only); build stays green.

**Files:** `Strings/en-US.xaml`, `es.xaml`, `zh-TW.xaml`, `zh-CN.xaml`, `bn.xaml`, `tr-TR.xaml`, `he.xaml`, `ar.xaml`, `ru.xaml`; `Services/Changelog.cs`

- [ ] **Step 1: Add the 11 keys to every locale file.** English (en-US) values verbatim:
```xml
<sys:String x:Key="Str_Tool_DocInfo">Document Info</sys:String>
<sys:String x:Key="Str_DocInfo_FldTitle">Title</sys:String>
<sys:String x:Key="Str_DocInfo_FldAuthor">Author</sys:String>
<sys:String x:Key="Str_DocInfo_FldSubject">Subject</sys:String>
<sys:String x:Key="Str_DocInfo_FldKeywords">Keywords</sys:String>
<sys:String x:Key="Str_DocInfo_FldCreator">Creator</sys:String>
<sys:String x:Key="Str_DocInfo_Save">Save</sys:String>
<sys:String x:Key="Str_DocInfo_Updated">Document info updated — save the file to keep your changes.</sys:String>
<sys:String x:Key="Str_DocInfo_Producer">Producer</sys:String>
<sys:String x:Key="Str_DocInfo_Pages">pages</sys:String>
<sys:String x:Key="Str_DocInfo_Created">created</sys:String>
```
Place them beside the existing `Str_Tool_*` keys. For the other 8 locales, translate the values (use that file's existing `sys:`/`s:` String prefix; keep "Document Info" recognizable, brand tokens Latin; he/ar translated naturally RTL). The `x:Key` attributes stay identical across all files.

- [ ] **Step 2: Changelog** — prepend one bullet to the newest `Release` in `Services/Changelog.cs`:
```
"New Document Info (Tools menu or F12): view and edit a PDF's title, author, subject, keywords, and creator, with a read-only summary of producer, page count, version, date, and size.",
```

- [ ] **Step 3: Build** — `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → `Build succeeded.`, 0 errors.

- [ ] **Step 4: Locale completeness** — `grep -L "Str_DocInfo_Created" Strings/*.xaml` → no output.

- [ ] **Step 5: Commit**
```bash
git add Strings/*.xaml Services/Changelog.cs
git commit -m "feat(docinfo): localization strings + changelog for Document Info

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `ShowDocumentInfo()` handler

**Files:** `MainWindow.Tools.cs`
**Interfaces — Consumes:** `RequireOpenDoc()`, `ShowToolForm(...)`, `ToolField`, `MarkDirty(bool)`, `SetStatus(string)`, `_doc`, `_currentFile`, `_originalFile`. **Produces:** `ShowDocumentInfo()`, `ToolsDocumentInfo_Click(object, RoutedEventArgs)`.

- [ ] **Step 1: Add the handler** to `MainWindow.Tools.cs` (in the `MainWindow` partial, beside the other `Tools*` handlers). Read each target's REAL surrounding patterns first (how other handlers call `ShowToolForm`, the exact `ToolField` ctor, and how `SetStatus`/`MarkDirty` are named):
```csharp
private static string L(string key) => Application.Current?.TryFindResource(key) as string ?? key;

private static string TryInfo(Func<string?> get)
{
    try { return get() ?? ""; } catch { return ""; }
}

private void ShowDocumentInfo()
{
    if (!RequireOpenDoc()) return;

    string title    = TryInfo(() => _doc!.Info.Title);
    string author   = TryInfo(() => _doc!.Info.Author);
    string subject  = TryInfo(() => _doc!.Info.Subject);
    string keywords = TryInfo(() => _doc!.Info.Keywords);
    string creator  = TryInfo(() => _doc!.Info.Creator);

    // read-only summary
    var parts = new List<string>();
    string producer = TryInfo(() => _doc!.Info.Producer);
    if (producer.Length > 0) parts.Add($"{L("Str_DocInfo_Producer")}: {producer}");
    try { parts.Add($"{_doc!.PageCount} {L("Str_DocInfo_Pages")}  ·  PDF {_doc.Version / 10}.{_doc.Version % 10}"); } catch { }
    try { var d = _doc!.Info.CreationDate; if (d != default) parts.Add($"{L("Str_DocInfo_Created")} {d:yyyy-MM-dd HH:mm}"); } catch { }
    try { var p = _originalFile ?? _currentFile; if (!string.IsNullOrEmpty(p) && File.Exists(p)) parts.Add($"{new FileInfo(p).Length / 1024.0:N0} KB"); } catch { }
    string note = string.Join("\n", parts);

    var fTitle    = new ToolField(L("Str_DocInfo_FldTitle"),    ToolFieldKind.Text, value: title);
    var fAuthor   = new ToolField(L("Str_DocInfo_FldAuthor"),   ToolFieldKind.Text, value: author);
    var fSubject  = new ToolField(L("Str_DocInfo_FldSubject"),  ToolFieldKind.Text, value: subject);
    var fKeywords = new ToolField(L("Str_DocInfo_FldKeywords"), ToolFieldKind.Text, value: keywords);
    var fCreator  = new ToolField(L("Str_DocInfo_FldCreator"),  ToolFieldKind.Text, value: creator);

    if (!ShowToolForm(L("Str_Tool_DocInfo"), new[] { fTitle, fAuthor, fSubject, fKeywords, fCreator },
                      L("Str_DocInfo_Save"), string.IsNullOrEmpty(note) ? null : note))
        return;

    _doc!.Info.Title    = fTitle.Value;
    _doc.Info.Author    = fAuthor.Value;
    _doc.Info.Subject   = fSubject.Value;
    _doc.Info.Keywords  = fKeywords.Value;
    _doc.Info.Creator   = fCreator.Value;
    MarkDirty(true);
    SetStatus(L("Str_DocInfo_Updated"));
}

private void ToolsDocumentInfo_Click(object sender, RoutedEventArgs e) => ShowDocumentInfo();
```
NOTE: verify the real signatures of `ToolField` ctor (`new ToolField(label, kind, value: ...)`), `MarkDirty`, and `SetStatus` in the codebase and adapt names if they differ. `MainWindow.Tools.cs` already `using`s `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Windows`, `System.Windows.Controls`. If a `L(...)` helper already exists in the class, do not redeclare it — reuse it.

- [ ] **Step 2: Build** — `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → 0 errors. (If a duplicate `L` or `TryInfo` already exists, remove the duplicate and reuse the existing one.)

- [ ] **Step 3: Commit**
```bash
git add MainWindow.Tools.cs
git commit -m "feat(docinfo): ShowDocumentInfo handler (view/edit metadata via ShowToolForm)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Invocation wiring (Tools menu + F12)

**Files:** `MainWindow.xaml`, `MainWindow.KeyboardShortcuts.cs`

- [ ] **Step 1: Tools menu item** — in `MainWindow.xaml`, find the `ToolsMenuBtn` `<ContextMenu>` (it lists `Str_Tool_Numbering`, `Str_Tool_Compress`, `Str_Tool_Ocr`, then a `<Separator/>`, then Redact/Protect/Sanitize). Add, among the non-destructive items (before the `<Separator/>`):
```xml
<MenuItem Header="{DynamicResource Str_Tool_DocInfo}" Click="ToolsDocumentInfo_Click"/>
```

- [ ] **Step 2: F12 key** — in `MainWindow.KeyboardShortcuts.cs` `OnPreviewKeyDown`, add an arm before the final `else if (e.Key == Key.Escape)`:
```csharp
else if (e.Key == Key.F12)
{
    ShowDocumentInfo();
    e.Handled = true;
}
```
(Place it after the other tool/navigation arms; ensure it sits inside the same if/else-if chain. The early `if (e.OriginalSource is TextBox) return;` guard already prevents F12 firing while typing in a field — leave it.)

- [ ] **Step 3: Build** — `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → 0 errors.

- [ ] **Step 4: Commit**
```bash
git add MainWindow.xaml MainWindow.KeyboardShortcuts.cs
git commit -m "feat(docinfo): open Document Info from Tools menu and F12

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Final verification

- [ ] **Step 1: Build** — `~/.dotnet/dotnet.exe build Scalpel.csproj -c Debug` → 0 errors, warnings ≤ 22.
- [ ] **Step 2: Tests** — `~/.dotnet/dotnet.exe test Scalpel.Tests/Scalpel.Tests.csproj` → 187 passing (no new tests; nothing broken).
- [ ] **Step 3: Locale completeness** — `grep -L "Str_Tool_DocInfo" Strings/en-US.xaml Strings/es.xaml Strings/zh-TW.xaml Strings/zh-CN.xaml Strings/bn.xaml Strings/tr-TR.xaml Strings/he.xaml Strings/ar.xaml Strings/ru.xaml` → no output.
- [ ] **Step 4: Wiring sanity** — `grep -rn "ToolsDocumentInfo_Click\|ShowDocumentInfo\|Key.F12" MainWindow.*.cs MainWindow.xaml` shows the handler defined once, the click handler referenced in XAML, and the F12 arm present.
- [ ] **Step 5: Manual smoke (owed to user — GUI cannot run headless):** open a PDF → F12 shows the form pre-filled with metadata + summary; Tools → Document Info shows the same; edit Title → Save → title-bar dirty mark appears → Ctrl+S → reopen → Title changed. A no-metadata PDF shows empty fields and a summary with pages/version/size.

## Self-review notes
- Spec coverage: localization+changelog (T1), handler/read/write/dirty (T2), Tools-menu + F12 invocation (T3), verification (T4). All spec sections mapped.
- Reuses `ShowToolForm` (no new dialog), edits in-memory `_doc.Info`, persists via existing save — matches spec.
- `L`/`TryInfo` guarded; `MarkDirty(true)` + `SetStatus` consistent with other Tools handlers. No new annotation types or pure logic ⇒ no unit test, per spec.
