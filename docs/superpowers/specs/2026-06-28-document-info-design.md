# Design Spec — Document Info (F12) (Tier 1, feature 2 of the KillerPDF port)

**Date:** 2026-06-28
**Status:** Approved (design), proceeding to plan
**Program:** see `killerpdf-feature-port-program` memory. Foundation refactor + Line tool already done.

## Goal

A themed dialog to **view and edit** the PDF's document-information metadata — Title, Author, Subject, Keywords, Creator — plus a read-only summary (Producer, page count, PDF version, creation date, file size). Opened from the **Tools menu** and by **F12**. Distinct from the existing `MetadataSanitizer`, which only *removes* metadata.

## Approach: reuse `ShowToolForm` — no new dialog UI

Scalpel already has a themed modal form builder, `ShowToolForm(string title, IReadOnlyList<ToolField> fields, string okText, string? note = null)` (`MainWindow.Tools.cs:76`). It renders `note` as a wrapped hint above the fields, seeds each `Text` field's TextBox from `ToolField.Value`, and on OK writes the edited text back into each `ToolField.Value` and returns true. This is the entire dialog — Document Info adds **no new dialog code**.

### New handler `ShowDocumentInfo()` (in `MainWindow.Tools.cs`)
1. `if (!RequireOpenDoc()) return;`
2. Read current metadata, each guarded (PdfSharpCore can throw on malformed Info):
   `string title = TryInfo(() => _doc!.Info.Title);` … for Title/Author/Subject/Keywords/Creator (helper returns `""` on null/throw).
3. Build the read-only **summary** `note` (each part guarded; skip empties), e.g. lines joined by `\n`:
   - `"{ProducerLabel}: {producer}"` (only if non-empty)
   - `"{pageCount} {PagesLabel}  ·  PDF {Version/10}.{Version%10}"`
   - `"{CreatedLabel} {CreationDate:yyyy-MM-dd HH:mm}"` (only if `CreationDate != default`)
   - `"{sizeKB:N0} KB"` from `new FileInfo(_originalFile ?? _currentFile).Length` (only if the file exists)
4. Build 5 `Text` `ToolField`s seeded with the read values, labels from localized strings:
   ```csharp
   var fTitle    = new ToolField(L("Str_DocInfo_FldTitle"),    ToolFieldKind.Text, value: title);
   var fAuthor   = new ToolField(L("Str_DocInfo_FldAuthor"),   ToolFieldKind.Text, value: author);
   var fSubject  = new ToolField(L("Str_DocInfo_FldSubject"),  ToolFieldKind.Text, value: subject);
   var fKeywords = new ToolField(L("Str_DocInfo_FldKeywords"), ToolFieldKind.Text, value: keywords);
   var fCreator  = new ToolField(L("Str_DocInfo_FldCreator"),  ToolFieldKind.Text, value: creator);
   ```
   where `L(key)` = `Application.Current?.TryFindResource(key) as string ?? key`.
5. `if (!ShowToolForm(L("Str_Tool_DocInfo"), new[]{fTitle,fAuthor,fSubject,fKeywords,fCreator}, L("Str_DocInfo_Save"), note)) return;`
6. Write back and mark dirty:
   ```csharp
   _doc!.Info.Title = fTitle.Value; _doc.Info.Author = fAuthor.Value;
   _doc.Info.Subject = fSubject.Value; _doc.Info.Keywords = fKeywords.Value;
   _doc.Info.Creator = fCreator.Value;
   MarkDirty(true);
   SetStatus(L("Str_DocInfo_Updated"));
   ```

**Save survival is confirmed:** Scalpel's save path (`MainWindow.FileToolbar.cs` `SaveInPlace`) calls `_doc.Save(target)` on the live in-memory `_doc`, so `_doc.Info.*` edits persist on a normal Ctrl+S. Document Info does not save the file itself — it edits in memory and marks dirty (consistent with the other Tools' dirty handling). The `MarkDirty(true)` makes the title-bar dirty mark appear so the user knows to save.

### Invocation
- **Tools menu** (`MainWindow.xaml`, the `ToolsMenuBtn` ContextMenu): add `<MenuItem Header="{DynamicResource Str_Tool_DocInfo}" Click="ToolsDocumentInfo_Click"/>` among the non-destructive ops (before the Redact `<Separator/>`). `ToolsDocumentInfo_Click` just calls `ShowDocumentInfo()`.
- **F12** (`MainWindow.KeyboardShortcuts.cs` `OnPreviewKeyDown`): add `else if (e.Key == Key.F12) { ShowDocumentInfo(); e.Handled = true; }` before the final `Escape` arm. (No F-keys are handled today; this is the first.)

## Localization (all 9 `Strings/*.xaml`)
New keys (English values shown; translate for es, zh-TW, zh-CN, bn, tr-TR, he, ar, ru; RTL he/ar included):
- `Str_Tool_DocInfo` = "Document Info"  (menu item AND dialog title)
- `Str_DocInfo_FldTitle` = "Title"
- `Str_DocInfo_FldAuthor` = "Author"
- `Str_DocInfo_FldSubject` = "Subject"
- `Str_DocInfo_FldKeywords` = "Keywords"
- `Str_DocInfo_FldCreator` = "Creator"
- `Str_DocInfo_Save` = "Save"
- `Str_DocInfo_Updated` = "Document info updated — save the file to keep your changes."
- `Str_DocInfo_Producer` = "Producer"
- `Str_DocInfo_Pages` = "pages"
- `Str_DocInfo_Created` = "created"
Every key must exist in every locale file or the `DynamicResource` (menu) / `TryFindResource` (code) lookup blanks/falls back in that language.

## Changelog
One user-facing bullet in the newest `Release` (`Services/Changelog.cs`): view/edit document properties (title, author, subject, keywords, creator) via Tools → Document Info or F12.

## Testing
- No extractable pure logic worth a unit test (read `_doc.Info` → form → write back). The `TryInfo` guard helper is trivial.
- **Build gate:** `~/.dotnet/dotnet.exe build` clean; full suite stays green (187).
- **Locale completeness check:** `grep -L "Str_Tool_DocInfo" Strings/*.xaml` returns nothing.
- **Manual smoke (owed to user — GUI can't run headless):** open a PDF → F12 opens the form showing current metadata; Tools → Document Info opens the same; edit Title; Save → title-bar dirty mark appears; Ctrl+S; reopen → Title changed. On a doc with no metadata the fields are empty and the summary still shows pages/version/size.

## Out of scope (YAGNI)
- No XMP metadata, no custom/extra properties, no editing Producer/CreationDate (kept read-only — they describe provenance).
- No separate ribbon button (Tools menu + F12 is enough).
- No multi-line growing fields (single-line TextBoxes scroll; fine for v1).

## Definition of done
F12 and Tools → Document Info both open a themed form pre-filled with the PDF's metadata + read-only summary; editing a field and clicking Save updates `_doc.Info`, marks the document dirty, and persists on the next save; build clean; 187 tests green; all 9 locales + changelog updated.
