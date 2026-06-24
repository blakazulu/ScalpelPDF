# Spec #3 — Hebrew text editing (bidi + font)

**Date:** 2026-06-24
**Status:** Design — awaiting user review

## Context

Third of the sequenced font/text sub-projects:

1. ✅ Font-detection toast + style preservation (shipped)
2. ✅ PdfSharpCore font resolver + embedding (shipped)
3. **Hebrew text editing (bidi + font)** ← *this spec*

### What's already handled (from investigation)

- **Page display of existing Hebrew: already works.** Pages are rasterized by
  pdfium (Docnet) — `MainWindow.xaml.cs:1637-1641` — which does full bidi/shaping.
  Existing Hebrew PDFs already view correctly. No work.
- **Search: probably works.** `Services/SearchService.cs:50-66` extracts logical-order
  text via PdfPig and matches with `Contains`; this matches Hebrew in typical
  logical-order PDFs. Needs a verification test, not a rewrite.
- **Editing / new text: broken.** PdfSharpCore `DrawString`
  (`MainWindow.xaml.cs:8079` new text, `:8121` edits) draws glyphs in code-point
  order left-to-right with no bidi reordering, and the new-text font (Geist, from
  Spec #2) has no Hebrew glyphs. This is the real work.
- **Form fields: out of scope.** The AcroForm fill path is hardwired to
  WinAnsi/Latin-1 (`EscapePdfString` replaces non-Latin chars with `?`,
  `:2969`). Unicode AcroForm is a separate, very-high-effort architecture change —
  deferred to a possible later spec.

## Goals

- Edited and newly-added Hebrew text burns into the PDF in correct **visual
  order** (bidi-reordered) using a font that has Hebrew glyphs, **right-aligned**.
- Editing existing Hebrew **keeps the original font when it actually covers Hebrew**,
  else falls back to a bundled Hebrew font.
- A Hebrew-capable font is **bundled** so Hebrew renders regardless of the machine.
- Hebrew **search** is verified working (with a documented limitation).
- The Latin/LTR path is unchanged.

## Non-goals

- Form-field (AcroForm) Hebrew — deferred (separate Unicode-encoding spec).
- A Hebrew UI locale (`he.xaml`) and global RTL UI mirroring — deferred (polish).
- A complete Unicode Bidirectional Algorithm — we implement a pragmatic run-based
  approximation (see Limitations).
- Arabic / other complex scripts (Arabic needs cursive shaping/joining — not covered).

## Components

### 1. `Services/BidiReorder.cs` — pure run-based reorderer

```csharp
public static class BidiReorder
{
    // Any Hebrew-block char (U+0590–U+05FF) or Hebrew presentation forms (U+FB1D–U+FB4F).
    public static bool ContainsRtl(string s);

    // Logical order -> visual (left-to-right glyph) order for DrawString.
    // No RTL chars -> returned unchanged. Base direction RTL when RTL present.
    public static string ToVisual(string logical);
}
```

**Algorithm (minimal bidi, base = RTL when RTL present):** classify each char as
`R` (Hebrew), `L` (Latin letter / other strong-LTR), `EN` (European digit 0-9), or
`N` (neutral: space, punctuation). Resolve neutral runs to the direction of the
surrounding strong context (between two R → R; between R and L → adopt base/RTL;
leading/trailing neutrals → base). Build maximal runs, then for base-RTL output:
emit runs right-to-left, reversing the characters within `R` runs, keeping `L`/`EN`
runs internally left-to-right. Pure, deterministic, no dependency.

**Limitations (documented):** not a complete UBA — nested directional embeddings,
explicit directional marks (LRM/RLM/LRE…), and some neutral-resolution edge cases
are approximated. Covers the common real cases: Hebrew sentences with embedded
numbers, dates, and Latin words.

### 2. `Services/TrueTypeCmap.cs` — glyph-coverage check (pure)

```csharp
public static class TrueTypeCmap
{
    // True if the font's cmap maps `codepoint` to a non-zero (real) glyph.
    // Parses the cmap table; supports formats 4 (BMP) and 12 (full). Defensive:
    // returns false on any malformed input. faceIndex for .ttc.
    public static bool CoversCodepoint(byte[] fontBytes, int codepoint, int faceIndex = 0);
}
```

Used to decide whether the original/resolved font has Hebrew glyphs (checks
U+05D0 ALEF as the representative). Sibling to the Spec #2 `TrueTypeName` parser
(reuses the same big-endian table-directory walk pattern).

### 3. `PdfFontResolver` addition — exact font bytes

```csharp
// True + bytes when `family` (with style) is an EXACT bundled or system-indexed
// face. Unlike GetFont, does NOT fall back to Arial — so a coverage check can't be
// fooled into testing Arial's glyphs for an unknown family.
public bool TryGetExactFontBytes(string family, bool bold, bool italic, out byte[] bytes);
```

### 4. Bundled Noto Sans Hebrew

Add `Resources/Fonts/NotoSansHebrew-Regular.ttf` (SIL OFL 1.1; the Hebrew Noto is
small — Hebrew block + basic Latin, so it renders mixed Hebrew/Latin from one
font). Register as `<Resource>` in `Scalpel.csproj` and via
`PdfFontResolver.Instance.RegisterBundledFont("Noto Sans Hebrew", bytes, false, false)`
in `App.RegisterPdfFonts()` (the Spec #2 startup hook). **Acquisition is an explicit
plan step**: download `NotoSansHebrew-Regular.ttf` from the notofonts GitHub release
(OFL) into `Resources/Fonts/`.

### 5. Drawn-text integration — one shared helper

Both burn-in paths call a new helper instead of `DrawString` directly:

```
DrawTextRun(gfx, text, candidateFontName, fontSize, brush, bounds, sx, sy):
  if not BidiReorder.ContainsRtl(text):
      draw as today (candidate font, left-aligned at bounds.Left)   # Latin unchanged
  else:
      font = (candidate covers Hebrew ?)  candidateFontName : "Noto Sans Hebrew"
            where "covers Hebrew" = PdfFontResolver.TryGetExactFontBytes(candidate,…)
                                    && TrueTypeCmap.CoversCodepoint(bytes, 0x05D0)
      visual = BidiReorder.ToVisual(text)
      width  = gfx.MeasureString(visual, xfont).Width
      x      = bounds.Right*sx - width            # right-align to bounds' right edge
      gfx.DrawString(visual, xfont, brush, x, baselineY)
```

- **Edited text** (`TextEditAnnotation`, `:8121`): candidate = `tea.FontName`
  (resolved by Spec #1, usually an installed family). Original font kept iff it
  exactly resolves AND covers Hebrew; else Noto. `bounds` = `tea.OriginalBounds`.
- **New text** (`TextAnnotation`, `:8079`): candidate = `"Geist"` (no Hebrew) → the
  coverage check fails → Noto, for Hebrew content. `bounds` from the annotation's
  position/width. Multi-line: reorder + align per line.

### 6. WPF entry polish

Where the edit/new TextBoxes are built, when their content contains Hebrew
(`BidiReorder.ContainsRtl`), set `FlowDirection = RightToLeft` and a Hebrew-capable
preview `FontFamily` (e.g. the bundled Noto via its pack family, or "Segoe UI") so
on-screen entry/editing aligns and renders correctly. WPF already shapes bidi; this
fixes alignment and the Geist-has-no-Hebrew preview gap.

### 7. Search

Add a verification test (below). No extraction change unless QA finds a real
failure. **Documented limitation:** text *we* burn in is stored in visual
(reordered) order, so searching our own edited Hebrew won't match logical-order
queries — acceptable for v1; existing real-world (logical-order) Hebrew PDFs search
fine.

## Data flow (burn-in)

```
text (logical) + candidate font
  → ContainsRtl? no → DrawString as today (Latin path untouched)
                 yes → coverage = TryGetExactFontBytes(candidate) && CoversCodepoint(ALEF)
                       font = coverage ? candidate : "Noto Sans Hebrew"
                       visual = ToVisual(text); right-align within bounds
                       DrawString(visual, font) → PdfSharpCore subsets+embeds (Spec #2)
```

## Error handling

`BidiReorder` and `TrueTypeCmap` never throw (defensive try/catch, safe defaults:
`ToVisual` returns input unchanged on failure; `CoversCodepoint` returns false).
`TryGetExactFontBytes` returns false on any miss. A failure anywhere degrades to the
existing behavior (draw as-is) rather than breaking a save.

## Testing

- **Unit (primary) — `BidiReorderTests`:** reorder table — pure Hebrew, Hebrew +
  number ("שלום 123"), Hebrew + Latin word, Hebrew + punctuation, pure Latin
  (unchanged), empty/null. `ContainsRtl` true/false cases.
- **Unit — `TrueTypeCmapTests`:** a known Hebrew-capable system font (e.g.
  `%WINDIR%\Fonts\segoeui.ttf` or the bundled Noto) covers U+05D0; a Latin-only font
  (the bundled Geist) does NOT; garbage bytes → false.
- **Integration — embedding:** draw a Hebrew string via the helper path through
  PdfSharpCore + Noto, save, reopen, assert a font program is embedded (reuse Spec
  #2's `HasEmbeddedFontProgram`).
- **Integration — search:** generate a PDF containing a known Hebrew word in logical
  order, confirm `SearchService` finds it.
- **Manual QA:** edit existing Hebrew text → correct visual order, right-aligned,
  original font kept when it has Hebrew; add a new Hebrew annotation → renders +
  embeds (Noto); mixed Hebrew+numbers reads correctly; Latin text unaffected.

## Files

- **New:** `Services/BidiReorder.cs`, `Services/TrueTypeCmap.cs`,
  `Scalpel.Tests/BidiReorderTests.cs`, `Scalpel.Tests/TrueTypeCmapTests.cs`,
  `Resources/Fonts/NotoSansHebrew-Regular.ttf`.
- **Edit:** `Services/PdfFontResolver.cs` (`TryGetExactFontBytes`),
  `App.xaml.cs` (register Noto), `MainWindow.xaml.cs` (the `DrawTextRun` helper +
  the two burn-in call sites + TextBox FlowDirection/preview-font),
  `Scalpel.csproj` (font `<Resource>`), `Scalpel.Tests/Scalpel.Tests.csproj` (link
  new source files), plus search/embedding test additions.

## Plan sequencing note

Suggested task order: (1) `BidiReorder` + tests; (2) `TrueTypeCmap` + tests; (3)
`TryGetExactFontBytes` + test; (4) bundle + register Noto (download step + embedding
test); (5) `DrawTextRun` helper wired into both burn-in sites; (6) WPF entry polish;
(7) search verification test. Each is independently testable.
