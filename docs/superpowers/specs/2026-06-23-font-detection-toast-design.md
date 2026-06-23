# Spec #1 — Font-detection toast + style preservation

**Date:** 2026-06-23
**Status:** Design — awaiting user review

## Context

This is the **first of four** sequenced sub-projects improving how Scalpel handles
fonts when editing PDF text. Agreed sequence:

1. **Font-detection toast + style preservation** ← *this spec*
2. Font embedding on save (portability)
3. Hebrew / RTL — full depth: edit + display + search (its own project)

Each sub-project gets its own spec → plan → implementation cycle.

### Problem this spec solves

When a user clicks existing PDF text to edit it, Scalpel already extracts the
original font name (PdfPig `letter.FontName`, `MainWindow.xaml.cs:6420`), cleans the
subset prefix and style suffixes (`:6428-6437`), and redraws on save with
`new XFont(tea.FontName, …)` (`:8039`). Two silent failures result:

- **Silent substitution.** If the extracted font isn't installed on the machine,
  WPF / `XFont` quietly substitutes another font. The edited line no longer matches
  the surrounding text and the user is never told.
- **Lost styling.** Bold/Italic are detected during cleaning and then *discarded*
  (`:6433-6435`), so editing bold or italic text returns it as regular.

## Goals

- Warn the user, at selection time, when the selected text's font is not installed —
  naming the font and stating a substitute will be used. **Warn only; never block editing.**
- Preserve Bold/Italic when editing existing text (live preview + on save).
- Move the font name/style/availability logic into a testable service.

## Non-goals (handled by later specs)

- Font embedding / subsetting on save (Spec #2).
- Hebrew / RTL / Unicode save path (Spec #3).
- A font picker for *new* text annotations.

## Architecture

### New service: `Services/FontResolver.cs`

The pure logic — normalize a raw PDF font name, detect style, decide if installed,
choose a usable family — moves out of the monolith into a WPF-light service, linked
into `Scalpel.Tests` via `<Compile Include>` like the other tested services.

```csharp
public sealed record ResolvedFont(
    string DisplayName,   // "Minion Pro" — cleaned, human-facing (toast)
    string FamilyName,    // family to hand to WPF / XFont (substitute if missing)
    bool IsBold,
    bool IsItalic,
    bool IsInstalled);    // matched against the available-family set

public static class FontResolver
{
    // availableFamilies is injected so tests don't depend on the host's fonts.
    public static ResolvedFont Resolve(
        string rawPdfFontName,
        IReadOnlyCollection<string> availableFamilies);
}
```

**Normalization** (the heart of the service, and where the test table lives). Real PDF
font names are PostScript-style and do not map 1:1 to WPF family names. Examples that
must normalize correctly:

| Raw PDF name             | DisplayName       | FamilyName        | Bold | Italic |
|--------------------------|-------------------|-------------------|------|--------|
| `ABCDEF+Minion-BoldItalic` | Minion          | Minion            | yes  | yes    |
| `TimesNewRomanPSMT`      | Times New Roman   | Times New Roman   | no   | no     |
| `TimesNewRomanPS-BoldMT` | Times New Roman   | Times New Roman   | yes  | no     |
| `Arial,BoldItalic`       | Arial             | Arial             | yes  | yes    |
| `Helvetica-Oblique`      | Helvetica         | Helvetica         | no   | yes    |
| `ArialMT`                | Arial             | Arial             | no   | no     |
| (empty / unparseable)    | Segoe UI          | Segoe UI          | no   | no     |

Normalization steps: strip `XXXXXX+` subset prefix → split on `,` and `-` → extract
style tokens (`Bold`, `Italic`, `Oblique`, `BoldItalic`) → drop PostScript suffixes
(`MT`, `PSMT`, `PS`) → trim → collapse internal CamelCase to spaced display name when
no spaces present and a known mapping applies (keep conservative; prefer leaving the
name as-is over a wrong guess).

`IsInstalled` = normalized family found (case-insensitive) in `availableFamilies`.
When not installed, `FamilyName` falls back to `"Segoe UI"` (current default) so the
substitute is explicit and consistent.

### Caller changes — `MainWindow.xaml.cs`

- **Selection (`:6418-6437`):** replace the inline cleaning with
  `FontResolver.Resolve(rawFont, availableFamilies)`. `availableFamilies` is built once
  from `Fonts.SystemFontFamilies` (+ the bundled Geist/Tabler) and cached. Apply
  `FamilyName`, `FontWeight` (from `IsBold`), `FontStyle` (from `IsItalic`) to the edit
  `TextBox`. If `!IsInstalled`, raise the toast (below).
- **Model (`Models/EditingTypes.cs`):** `TextEditAnnotation` gains
  `bool IsBold` and `bool IsItalic`.
- **Save redraw (`:8039`):** build
  `new XFont(tea.FontName, tea.FontSize * sy, ToXFontStyle(tea.IsBold, tea.IsItalic))`.

### Toast UI

A small, transient, auto-dismissing overlay (chosen design):

- `StudioOverlayCard`-styled banner, top-center, ~4s auto-dismiss (DispatcherTimer),
  fade in/out.
- Content: warning glyph + "{DisplayName} isn't installed — a substitute font will be
  used." + a **Copy name** button (copies `DisplayName` to clipboard; we cannot legally
  fetch/install fonts for the user).
- Implemented as a reusable `ShowToast(string message, string? copyText = null)` helper
  so future features can reuse it. Also mirrors the message to `SetStatus` is **out of
  scope** (overlay only, per decision).
- All strings localized: new keys `Str_FontMissing_Body`, `Str_FontMissing_CopyName`
  (and any format string) added to **all six** locale files
  (`en-US, es, zh-TW, zh-CN, bn, tr-TR`) per the localization rule. Untranslated
  languages may use the English string as placeholder, but the key must exist in every
  file.

## Data flow

```
user clicks text run
  → PdfPig letter/word FontName (raw)
  → FontResolver.Resolve(raw, availableFamilies)
       → ResolvedFont { DisplayName, FamilyName, IsBold, IsItalic, IsInstalled }
  → apply FamilyName/Weight/Style to edit TextBox (live preview)
  → store FamilyName + IsBold + IsItalic on the TextEditAnnotation
  → if !IsInstalled: ShowToast(DisplayName)
  → on save: XFont(annotation.FontName, size, style flags) → DrawString
```

Note: the annotation stores the resolved family (`FontName`, the family actually
applied to the TextBox) plus the style flags, so save and live preview stay consistent.

## Error handling

Follow the codebase's defensive pattern: `FontResolver.Resolve` never throws — any
parse failure returns the safe default (`Segoe UI`, no style, `IsInstalled = true` so no
spurious toast). Building `availableFamilies` is wrapped in try/catch with a minimal
fallback set. Toast failures are swallowed (a missing toast must never break editing).

## Testing

- **`FontResolverTests` (xUnit, primary):** the normalization table above plus edge
  cases (empty, only-subset-prefix, multiple style tokens, unknown suffixes). Drive the
  implementation TDD-style. `availableFamilies` injected, so `IsInstalled` is
  deterministic and host-independent.
- **`FontResolver` installed-detection tests:** pass a fixed family set, assert
  `IsInstalled` and substitute `FamilyName` for present vs missing fonts.
- **Manual QA (documented, not automated):** open a PDF using a non-installed font,
  confirm toast names it and offers copy; edit bold + italic text and confirm style
  round-trips through save. Add to QA notes.

## Files touched

- **New:** `Services/FontResolver.cs`, `Scalpel.Tests/FontResolverTests.cs`
- **Edit:** `MainWindow.xaml.cs` (selection path, save redraw, `ShowToast` helper),
  `MainWindow.xaml` (toast overlay element), `Models/EditingTypes.cs`
  (`IsBold`/`IsItalic`), `Scalpel.Tests/Scalpel.Tests.csproj` (link new source),
  six `Strings/*.xaml` locale files.
