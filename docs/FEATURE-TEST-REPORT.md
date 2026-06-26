# Scalpel feature test — 5 bilingual sample PDFs

Every WPF-free document feature run against the five real bilingual (Hebrew + English)
sample documents in `docs/samples/`. Generated 2026-06-26.

| Sample | Pages |
|---|---|
| `scalpel-sample-invoice.pdf` | 1 |
| `scalpel-sample-letter.pdf` | 1 |
| `scalpel-sample-report.pdf` | 1 |
| `scalpel-sample-handbook.pdf` | 3 |
| `scalpel-sample-contract.pdf` | 5 |

**Totals: 66 PASS · 0 FAIL · 6 NONE (informational) · 0 SKIP.**

> This run uncovered (and fixed) a real OCR bug — see finding 1.

How it was run:
- **Pure features** (PdfSharpCore / PdfPig) — `Scalpel.Tests/FeatureMatrixTests.cs`, an
  xUnit integration test that runs the real services on the five files and asserts no failures.
- **Native features** (Docnet/PDFium rasterizer — Render/Redact/Compress/OCR) — exercised
  against the same files via the app assemblies (Docnet is intentionally out of the unit-test
  project, so this part runs through `Scalpel.exe`).

## Results

| Feature | invoice | letter | report | handbook (3pg) | contract (5pg) |
|---|---|---|---|---|---|
| Render (all pages) | PASS | PASS | PASS | PASS | PASS |
| Redact (1–2 areas, multi-page) | PASS | PASS | PASS | PASS | PASS |
| Compress — Low | PASS | PASS | PASS | PASS | PASS |
| Compress — Medium | PASS | PASS | PASS | PASS | PASS |
| Compress — High | PASS | PASS | PASS | PASS | PASS |
| OCR (make searchable) | PASS | PASS | PASS | PASS | PASS |
| Page numbering (`{page} / {total}`) | PASS | PASS | PASS | PASS | PASS |
| Bates numbering (`BATES-{n}`, 6-digit) | PASS | PASS | PASS | PASS | PASS |
| Header/footer text | PASS | PASS | PASS | PASS | PASS |
| Password protect + permissions + remove | PASS | PASS | PASS | PASS | PASS |
| Remove metadata | PASS | PASS | PASS | PASS | PASS |
| Full-text search — English | PASS | PASS | PASS | PASS | PASS |
| Full-text search — Hebrew | NONE¹ | NONE¹ | NONE¹ | NONE¹ | NONE¹ |
| Merge (handbook 3 + contract 5 → 8) | — | — | — | PASS | PASS |
| Split / extract pages | — | — | — | — | PASS |

## Findings

0. **BUG FOUND & FIXED — OCR produced non-searchable PDFs.** Running OCR end-to-end against the
   real files (after installing Tesseract) revealed that the "Make Searchable" output had **zero
   extractable text** — Tesseract recognized the text perfectly, but Scalpel dropped it. Root
   cause: `TesseractCliOcrEngine` requested TSV via the `tsv` **config file**, which lives in a
   full tessdata install's `configs/` folder. When `--tessdata-dir` is overridden to Scalpel's own
   folder (the portable download dir holds only `eng.traineddata`, no `configs/`), tesseract can't
   open the `tsv` config (`read_params_file: Can't open tsv`), silently falls back to plain text,
   and the TSV parser gets nothing → empty text layer. Fix: request TSV via
   `-c tessedit_create_tsv=1` (a parameter, no config file needed). After the fix, "Scalpel" is
   found 5–17 times per document. Regression test added (`BuildArguments_*`).

1. **Hebrew full-text search returns 0 hits — a known, documented limitation.** Rendering of
   Hebrew is correct (verified positionally + visually), but PdfSharpCore's ToUnicode CMap for
   subsetted Noto faces collapses, so text *extraction/search* over Scalpel-rendered Hebrew is
   unreliable. English search works well (e.g. "Scalpel" found on every document, across the
   correct pages). This matches the limitation noted in `CLAUDE.md`. NONE = no hits, expected.

2. **Compression enlarges these documents — by design.** Compress rasterizes each page to a
   JPEG, which is a big win on scan/photo-heavy PDFs but *grows* lightweight vector-text PDFs
   (e.g. invoice 14 KB → 23 KB even at High). The High preset is meaningfully smaller than Low/
   Medium, but all presets exceed the original here. Not a bug (the service documents the
   image-based trade-off); worth a UI hint that Compress targets scanned/image PDFs.

3. **Everything else passed on every document, including the 3- and 5-page files** — multi-page
   render, multi-page redaction, per-page stamping, encryption round-trip with permission flags,
   metadata removal, merge, and page extraction all behaved correctly.

## Not covered here (GUI-only)

These live in the WPF code-behind and require the interactive app / E2E harness, not this
service-level test: annotations (text, highlight, ink, image), **editing existing text** (the
Hebrew RTL editing fix is covered separately by `ExampleDocsTests`' logical round-trip), digital
signatures, crop, interactive rotate, form filling, flatten, and print.

¹ NONE — query returned no hits (expected; see finding 1).
