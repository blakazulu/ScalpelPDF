# PDF Feature Opportunities — Top 5 Local-Only Wins

*Research summary · 2026-06-25*

**Question asked:** What are the top things people are trying to do with PDFs but
*can't* — either because it's locked behind a paywall/subscription, or because no
app offers it well — that Scalpel could do **100% locally** (no online services, no
paid APIs, no telemetry)?

**Method:** Web research across "best PDF editor" roundups (TechRadar, PCWorld,
Zapier, G2), Adobe/Smallpdf/iLovePDF pricing and help pages, user-frustration
threads (Quora, Acrobat user forums), and the open-source/offline-tool landscape
(OCRmyPDF, NAPS2, Ghostscript-WASM compressors). Each candidate was then filtered
against Scalpel's actual tech stack (Docnet/**PDFium** render, **PdfSharpCore**
structure read/write, **PdfPig** text extraction — all already bundled and offline).

---

## The recurring theme

Across every source, the *basics* (view, annotate, merge/split, simple overlay
text, sign) are free everywhere — Scalpel already does all of these. The pain is
concentrated in a predictable cluster of "professional" features that the big
tools (Adobe Acrobat Pro, Smallpdf, iLovePDF, Foxit) deliberately put **behind a
subscription**, *and* that the free web tools solve only by **uploading your file
to their server** — a non-starter for anyone with a confidential document.

That intersection — *paywalled in desktop apps* **and** *privacy-hostile in free
web tools* — is exactly Scalpel's lane: **local-only, portable, no telemetry, free.**

---

## Top 5 opportunities (ranked by demand × paywall-pain × local feasibility)

### 1. Offline OCR — make a scanned PDF searchable & selectable
**The pain.** OCR is the single most consistently paywalled feature. Smallpdf
requires Pro for OCR; iLovePDF gates it; Acrobat only does it during the trial.
Every free web OCR tool wants you to upload the scan. Roundups repeatedly call out
"built-in OCR" as the feature missing from free desktop editors.

**Why users can't get it free + private today.** The good offline answers
(OCRmyPDF, Tesseract, NAPS2) are command-line or scanner-centric — there's no
polished, portable Windows *editor* that just says "make this scan searchable."

**Feasibility in Scalpel — HIGH.** PDFium already rasterizes every page to a
bitmap (the render path Scalpel uses today). Feed that bitmap to a bundled
**Tesseract** engine (the `charlesw/Tesseract` .NET wrapper runs on net48 and
ships `tessdata` locally), then write the recognized words back as an invisible
text layer over the image via PdfSharpCore. 100% offline, no upload, no fee.
*Cost note:* language data files add a few MB per language — fits the portable model.

---

### 2. True redaction + "sanitize document" (strip hidden text & metadata)
**The pain.** Real redaction — *permanently deleting* the underlying text and
hidden data, not just drawing a black box over it — is Acrobat **Pro only**.
Users who "redact" with a free annotation tool routinely leak the text underneath
(it's still selectable/copyable). Sources stress that true redaction must also
remove metadata, author info, and revision history ("Sanitize Document").

**Why it matters.** This is a genuine privacy/legal hazard, not a convenience.
It's the #1 thing people get *wrong* with free tools.

**Feasibility in Scalpel — HIGH.** Scalpel already white-outs original bounds when
editing text and flattens annotations on save — the redaction primitive is half
built. A guaranteed-safe redaction: render the page region via PDFium, paint it
solid black, and **flatten that area to an image** so no recoverable text remains
underneath. Pair it with a one-click **metadata wipe** (PdfSharpCore exposes the
document `Info` dictionary and XMP — trivial to clear author/title/producer/dates).
This leverages Scalpel's existing flatten + burn-in pipeline.

---

### 3. Local PDF compression — shrink file size offline
**The pain.** "Reduce file size" is paywalled in Acrobat's desktop app and the
free web compressors all upload your file (or run Ghostscript-WASM in-browser as a
workaround). People need it constantly to meet email/upload size limits — and
they're handing confidential files to a website to do it.

**Feasibility in Scalpel — MEDIUM-HIGH.** The dominant cost in most PDFs is
embedded images. Scalpel can walk the document with PdfSharpCore, re-encode/
downsample oversized images (re-compress as JPEG at a chosen DPI/quality), drop
unused objects, and rewrite. A simpler-but-robust fallback already available via
the existing stack: PDFium-render each page and rebuild — the same
"rasterize-rebuild" path the loader uses for damaged files — with a quality slider.
Offer a "Compress" action with Low/Medium/High presets and a before/after size.

---

### 4. Password-protect / encrypt (and remove a known password)
**The pain.** Encrypting a PDF with a password, or *removing* a password you
legitimately know, is a paid feature in most editors and a privacy-risky upload in
free web tools. It's a common, simple need (sending something sensitive by email)
that users are surprised costs money.

**Feasibility in Scalpel — MEDIUM-HIGH.** Scalpel *already* decrypts PDFs to a
temp copy as part of its open/save fallback chain (PDFium strips encryption;
PdfSharpCore can read but not re-save encrypted files). Adding the inverse —
PdfSharpCore `PdfDocumentSecuritySettings` (user/owner password, permission flags)
on save — closes the loop and reuses code Scalpel maintains anyway. "Remove
password" is just: open with the user's password → save without security.

---

### 5. Bates numbering / page numbers / headers & footers
**The pain.** Bates numbering (sequential per-page IDs for legal/discovery work)
is Acrobat Pro / Foxit ($9+/mo) territory; the "free" Bates tools stamp a
watermark on your output unless you pay. General page numbers and header/footer
text are also commonly gated. This is a high-value niche (law firms, accountants)
with a strongly underserved free market.

**Feasibility in Scalpel — HIGH (easiest of the five).** Scalpel already burns
arbitrary text onto pages at save time (`DrawTextRun`, the annotation flatten
path, with full multilingual/RTL support). Bates/page numbering is just a
per-page counter formatted with an optional prefix/suffix and placed in a chosen
corner — a thin UI on top of machinery that already exists. No new library needed.

---

## At a glance

| # | Feature | Paywall pain | Privacy angle | Local feasibility | Reuses existing Scalpel code |
|---|---------|:---:|:---:|:---:|---|
| 1 | Offline OCR (searchable scans) | Very high | High (no upload) | High | PDFium render → +Tesseract → text layer |
| 2 | True redaction + sanitize | High (Pro only) | Very high | High | Flatten / burn-in / white-out pipeline |
| 3 | Local compression | High | High (no upload) | Med-High | PdfSharpCore image re-encode / rasterize-rebuild |
| 4 | Encrypt / remove password | Medium | Medium | Med-High | Existing decrypt path + PdfSharpCore security |
| 5 | Bates / page numbers / headers | High (legal niche) | — | High | `DrawTextRun` per-page burn-in |

## Recommendation

Highest impact-to-effort, in order:

1. **Bates / page numbers / headers** — almost pure UI over code Scalpel already
   has; ship it first as a quick, differentiating win for the legal/business niche.
2. **True redaction + metadata sanitize** — strong privacy story that matches
   Scalpel's "local-only, no telemetry" identity; reuses the flatten pipeline.
3. **Encrypt / remove password** — small, completes the encryption loop Scalpel
   half-owns already.
4. **Offline OCR** — the headline feature users want most, but the biggest lift
   (new Tesseract dependency + language data + invisible-text-layer plumbing).
5. **Local compression** — valuable and feasible; tune quality presets carefully
   to avoid degrading scans.

All five are achievable **entirely offline** with Scalpel's current three-library
stack (plus a bundled Tesseract engine for #1), keeping the app's local-only,
portable, free, GPLv3 promise intact.

---

## Sources

- [I tried a dozen free PDF editors and finally found the best one — XDA](https://www.xda-developers.com/i-tried-a-dozen-free-pdf-editors-and-finally-found-the-best-one/)
- [This is the only Adobe Acrobat alternative I'll ever need — XDA](https://www.xda-developers.com/pdfgear-adobe-acrobat-alternative/)
- [Best PDF editor 2026 — TechRadar](https://www.techradar.com/best/pdf-editors)
- [Best free PDF editor 2026 — TechRadar](https://www.techradar.com/best/free-pdf-editor)
- [Best PDF editors 2026: Premium, budget, and free — PCWorld](https://www.pcworld.com/article/407214/best-pdf-editors.html)
- [Zero-subscription PDF editing with OCR and redaction for $39.99 — PCWorld](https://www.pcworld.com/article/2931385/get-zero-subscription-pdf-editing-with-ocr-and-redaction-tools-for-39-99.html)
- [The 7 best PDF editor apps — Zapier](https://zapier.com/blog/best-pdf-editor-apps/)
- [8 Best PDF Editors I Found After Testing 20 Tools — G2](https://learn.g2.com/best-pdf-editor)
- [I trust these open-source apps for all my PDF editing — How-To Geek](https://www.howtogeek.com/i-trust-these-open-source-apps-for-my-pdf-editing/)
- [Why is Adobe so greedy? — Quora](https://www.quora.com/Why-is-Adobe-so-greedy-They-literally-have-nothing-free-At-least-with-other-software-they-let-you-use-some-features-before-charging-you-subscription)
- [Why does Adobe PDF Pack ask for subscription now — Acrobat user forum](https://answers.acrobatusers.com/Why-Adobe-PDF-Pack-subscription-payment-week-I-didn-pay-sending-Web-pag-q241467.aspx)
- [Redact sensitive content in PDFs in Acrobat Pro — Adobe](https://helpx.adobe.com/acrobat/using/removing-sensitive-content-pdfs.html)
- [How To Remove Metadata From a PDF — Redactable](https://www.redactable.com/blog/how-to-remove-metadata-from-pdf)
- [Redact PDF Online — Smallpdf](https://smallpdf.com/redact-pdf)
- [OCRmyPDF / Tesseract offline OCR — ArchivEye (GitHub)](https://github.com/eastrd/ArchivEye)
- [Local LLM PDF OCR — 100% offline (GitHub)](https://github.com/ahnafnafee/local-llm-pdf-ocr)
- [8 Best Free PDF OCR Software in 2026 — SoftPicker](https://softpicker.com/best-free-pdf-ocr-software/)
- [iLovEPDF pricing (OCR/compress/redact gating)](https://www.ilovepdf.com/pricing)
- [Compress PDF (server-side) — Smallpdf](https://smallpdf.com/compress-pdf)
- [Compress PDF without upload (in-browser Ghostscript-WASM) — ihatepdf](https://www.ihatepdf.cv/compress-pdf)
- [Add Bates numbering to PDFs in Acrobat Pro — Adobe](https://helpx.adobe.com/acrobat/desktop/edit-documents/apply-bates-numbering/add-bates.html)
- [Free methods to add Bates numbers (watermark on free tier) — Aryson](https://www.arysontechnologies.com/blog/free-methods-to-add-bates-numbers-stamp-to-pdf-file/)
