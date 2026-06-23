# Spec #2 — PdfSharpCore font resolver + embedding verification

**Date:** 2026-06-24
**Status:** Design — awaiting user review

## Context

Second of four sequenced font-handling sub-projects:

1. ✅ Font-detection toast + style preservation (shipped — `2026-06-23-font-detection-toast-design.md`)
2. **PdfSharpCore font resolver + embedding verification** ← *this spec*
3. Hebrew / RTL — full depth: edit + display + search (its own project)

### What we learned during brainstorming (re-baseline)

PdfSharpCore 1.3.67 on .NET Framework 4.8 / Windows uses a **built-in GDI font
resolver** and **automatically subsets + embeds** any font it draws with — this is
the default, no configuration. So:

- Editing text whose font **is installed** already produces a saved PDF with the font
  embedded; it already travels correctly to machines without that font. The original
  "embedding is missing" premise was wrong.
- Editing text whose font **is not installed** substitutes Segoe UI and the Spec #1
  toast warns. We cannot embed a font the machine does not have, so this case is
  correctly handled by the toast, not by embedding.

The genuine remaining gaps this spec addresses:

1. **No automated proof of embedding.** It relies on an undocumented PdfSharpCore
   default; a regression could silently break portability with no test to catch it.
2. **New text annotations are inconsistent.** New text boxes preview in the bundled
   **Geist** font but burn in as hardcoded `"Segoe UI"` (`MainWindow.xaml.cs:8070`) —
   a WYSIWYG mismatch; Geist never embeds because PdfSharpCore cannot see WPF resource
   fonts.
3. **No custom font resolver.** Bundled `.ttf`s (Geist now, Noto Hebrew in Spec #3)
   are invisible to PdfSharpCore. A registered `IFontResolver` serving system +
   bundled fonts fixes #2 and is the prerequisite for guaranteed Hebrew rendering.

## Goals

- Register a PdfSharpCore `IFontResolver` that serves **both** all installed system
  fonts **and** bundled application fonts, so PdfSharpCore can embed either.
- Draw new text annotations in **Geist** (bundled, embedded), matching the editor
  preview — fixing the WYSIWYG mismatch and exercising the bundled-font path.
- Add an automated test that proves saved PDFs carry embedded font programs, guarding
  against regressions.

## Non-goals (out of scope)

- A font picker for new text annotations (still deferred).
- Honoring per-font embedding-restriction bits (`fsType`) — rely on PdfSharpCore
  defaults; revisit only if a real licensing need arises.
- Hebrew / RTL / bidi (Spec #3) — though the resolver built here is its foundation.
- Changing how *editing existing* text picks its font (Spec #1 already resolves and
  substitutes); this spec only ensures the resolved family can be embedded.

## Critical constraint that shapes the whole design

`GlobalFontSettings.FontResolver` is **process-global, settable once**, and overriding
it **disables PdfSharpCore's built-in GDI resolver**. There is no public way to chain
or delegate to the default on 1.3.67. Therefore our resolver MUST reimplement system
font resolution (find the TTF file for an arbitrary installed family) — otherwise
existing flows that draw "Arial"/"Segoe UI" (e.g. `SampleDocument.cs`, edited text)
break. This is the central technical risk and is de-risked by a spike-first plan.

## Architecture

### New service: `Services/PdfFontResolver.cs`

Implements `PdfSharpCore.Fonts.IFontResolver`. Pure of WPF — bundled font bytes are
injected, not read from `pack://` inside the resolver, so it works headless in tests.

```csharp
public sealed class PdfFontResolver : IFontResolver
{
    public static PdfFontResolver Instance { get; }

    // Register a bundled font's bytes for a family + style. Called at app startup
    // (with pack:// bytes) and from tests (with on-disk bytes). Idempotent per key.
    public void RegisterBundledFont(string family, byte[] bytes, bool bold, bool italic);

    // IFontResolver:
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic);
    public byte[] GetFont(string faceName);
    // (DefaultFontName getter if the interface version requires it.)
}
```

**Two font sources, resolved in order:**

1. **Bundled registry** — an in-memory `Dictionary<faceKey, byte[]>` populated via
   `RegisterBundledFont`. Checked first so app fonts always win over a same-named
   system font.
2. **System font index** — lazily built once on first resolve (not at startup, to
   avoid slowing launch): scan `%WINDIR%\Fonts` for `*.ttf`/`*.ttc`/`*.otf`, parse
   each file's TrueType `name` table to read family (name ID 1) + subfamily (name ID
   2), derive `(family, bold, italic)`, and map to `(filePath, ttcFaceIndex)`. Cache
   the map. `.ttc` collections contain multiple faces — index each. Style-linked
   files (e.g. `arialbd.ttf` for Arial Bold) are captured naturally because each file
   declares its own subfamily.

**`ResolveTypeface(family, bold, italic)`:**
- Look up exact `(family, bold, italic)` in bundled, then system.
- If the family exists but not the exact style, return the **regular** face of that
  family with the requested `bold`/`italic` flags set on `FontResolverInfo` so
  PdfSharpCore simulates the missing style.
- If the family is unknown entirely, fall back to a guaranteed-present system font
  (Arial; if Arial is somehow absent, the first indexed family). Never returns null.

**`GetFont(faceName)`** returns the font program bytes for the resolved face:
bundled bytes directly, or the bytes read from the system file (extracting the right
sub-font from a `.ttc` by face index). Wrapped so a read failure falls back to the
default face rather than throwing.

**Defensive:** the index build, every file parse, and every byte read are wrapped in
try/catch; a malformed font file is skipped, not fatal. A fully failed index leaves
only the bundled registry + the Arial fallback path.

### TrueType `name`-table parser

A small pure helper (in `PdfFontResolver.cs` or a sibling `Services/TrueTypeName.cs`)
that, given font file bytes (and an optional TTC face index), returns
`(familyName, subfamilyName)` by reading the `name` table. Pure and unit-testable
against a known system font file. This is the unit most worth isolating and testing.

### Registration — `App.xaml.cs`

In `App.OnStartup`, before any `XFont`/PDF work:

```csharp
PdfFontResolver.Instance.RegisterBundledFont("Geist", <geist regular bytes>, false, false);
// (+ bold/italic Geist faces if those ttf variants are bundled)
GlobalFontSettings.FontResolver = PdfFontResolver.Instance;   // once, guarded
```

Geist bytes come from the existing `pack://application:,,,/Resources/Fonts/...`
resource via `Application.GetResourceStream`. The set is behind an idempotent guard
(`if (GlobalFontSettings.FontResolver is null)`) so test setup can run the same
registration without the double-set exception. Geist is SIL OFL 1.1 — embedding in
user PDFs is permitted; ship the license text per OFL terms if not already present.

### New-text-annotation change — `MainWindow.xaml.cs`

At the `case TextAnnotation ta:` save path (`:8070`), change
`new XFont("Segoe UI", ta.FontSize * sy)` to `new XFont("Geist", ta.FontSize * sy)`
(preserving any existing style). The editor preview already uses the `FontUI`
(Geist) resource, so preview and output now match, and the new text embeds Geist via
the resolver.

## Data flow

```
app startup:
  read Geist pack:// bytes → PdfFontResolver.RegisterBundledFont("Geist", …)
  GlobalFontSettings.FontResolver = PdfFontResolver.Instance   (guarded, once)

save (burn-in) for any text:
  new XFont(family, size, style)
    → PdfSharpCore asks resolver.ResolveTypeface(family, bold, italic)
        → bundled registry? else system index (lazy-built) ? else Arial fallback
    → resolver.GetFont(faceKey) → byte[]
    → PdfSharpCore subsets + embeds the program into the saved PDF
```

## Error handling

Follows the codebase's defensive norm. `ResolveTypeface`/`GetFont` never throw and
never return null. System-index construction, per-file `name`-table parsing, and font
byte reads each swallow and skip on failure. Registration failures (e.g. Geist
resource missing) are caught and logged; the app still runs using system fonts only.

## Testing

- **Unit — `TrueTypeNameTests`:** parse a known system font file (e.g.
  `%WINDIR%\Fonts\arial.ttf`) and assert family `"Arial"` / regular subfamily; parse a
  bold file and assert the bold subfamily. (Guard: skip with a clear message if the
  file is absent, so CI on a non-Windows runner doesn't hard-fail — but this is a
  Windows project so the path is expected present.)
- **Unit — `PdfFontResolverTests`:** with a resolver whose system index is built,
  `ResolveTypeface("Arial", false, false)` resolves to an Arial face;
  `ResolveTypeface("Arial", true, false)` resolves to a bold face or a simulated-bold
  regular; an unknown family resolves to the Arial fallback (never null);
  `RegisterBundledFont` makes a bundled family resolve to the registered bytes and win
  over system.
- **Integration — `FontEmbeddingTests` (the guarantee):** register the resolver
  (idempotent setup), build a `PdfDocument`, draw a line of Arial text (system path)
  and a line of a bundled font (bytes read from the repo
  `Resources/Fonts/Geist*.ttf`), `Save` to a temp file, reopen with **PdfPig**, and
  assert the page's font descriptors include an embedded font program (`FontFile2`
  for the TrueType faces). Cleans up the temp file. Runs headless — no WPF
  `Application` required. This is the regression guard for portability.
- **Manual QA (documented):** in the app, add a new text annotation → save → reopen
  and confirm the text renders in Geist and the font is embedded (e.g. inspect with a
  PDF tool or open on a machine without Geist). Edit existing installed-font text →
  confirm it remains embedded. Confirm `SampleDocument` / sample generation still
  draws Arial correctly after the resolver override.

## Files

- **New:** `Services/PdfFontResolver.cs` (+ optional `Services/TrueTypeName.cs`),
  `Scalpel.Tests/TrueTypeNameTests.cs`, `Scalpel.Tests/PdfFontResolverTests.cs`,
  `Scalpel.Tests/FontEmbeddingTests.cs`.
- **Edit:** `App.xaml.cs` (registration), `MainWindow.xaml.cs:8070` (Geist for new
  text), `Scalpel.Tests/Scalpel.Tests.csproj` (link new source files + reference
  PdfSharpCore/PdfPig, both already referenced).

## Plan sequencing note

The implementation plan must start with a **spike (Task 1):** a minimal resolver
serving a single hard-coded system font (Arial), registered, proven by a
save → PdfPig → "FontFile2 present" assertion. This confirms the exact `IFontResolver`
interface shape in PdfSharpCore 1.3.67 (`ResolveTypeface` signature, `FontResolverInfo`
constructor, whether `DefaultFontName` is required) and that the GDI-override approach
embeds correctly — before building the full system-font index. If the spike reveals
the override cannot embed system fonts acceptably, stop and re-evaluate scope with the
user before proceeding.
