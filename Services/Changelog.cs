using System.Collections.Generic;

namespace Scalpel.Services
{
    /// <summary>
    /// Consumer-facing release notes shown in the "What's New" popup, newest first.
    /// Plain data — edit this list to add a release (keep entries short and user-friendly).
    /// </summary>
    public static class Changelog
    {
        public sealed record Release(string Version, string Date, string[] Changes);

        public static IReadOnlyList<Release> Releases { get; } = new[]
        {
            new Release("2.1.0", "July 2026", new[]
            {
                "New OCR Region tool (Tools ▸ OCR Region): drag a rectangle over any part of a page to copy just that area's text to the clipboard — handy for grabbing a single paragraph, table cell, or caption from a scan.",
                "Digitally Sign can now use a certificate already installed in Windows — pick \"Windows certificate store\" and choose from a list (smart cards and enterprise-issued certificates included), instead of locating a .pfx/.p12 file and typing its password.",
                "Digitally Sign can optionally add a trusted RFC-3161 timestamp, so the signature proves *when* it was made and stays valid even after your certificate expires. Tick \"Add a trusted timestamp\" when signing (it contacts a timestamp authority over the internet).",
                "Digitally Sign can now show a visible signature on the first page — a small captioned box (signer name, date, and an optional reason) placed in the corner you choose, instead of an invisible signature. Pick a corner under \"Visible signature\" when signing.",
                "Digitally Sign can optionally embed long-term validation (LTV) info — the certificate chain plus any reachable revocation lists — into the signed PDF, so it can still be validated years later. Tick \"Embed long-term validation info\" (best paired with a trusted timestamp and a CA-issued certificate).",
            }),
            new Release("2.0.0", "June 2026", new[]
            {
                "Make Searchable (OCR) now shows live progress (\"Recognizing page X of Y\") and can be cancelled with the button or Esc, so long scans no longer leave you waiting with no feedback.",
                "The saved-signatures chooser now drops down directly beneath the Sign button and follows your theme (it used to float in the page and stay dark in Light mode); clicking away closes it cleanly.",
                "New Digitally Sign tool (Tools menu): add a real, invisible cryptographic signature to a PDF using your own certificate (.pfx/.p12). Scalpel appends the signature without re-writing the rest of the file, so the signed copy stays valid in PDF readers — all on your machine, no online service.",
                "Document tabs: open several PDFs and switch between them from a tab strip above the page — click a tab, press Ctrl+Tab to cycle, or close one with its ×. Switching prompts you to save any unsaved changes first. The strip stays hidden until you have more than one file open.",
                "Form fields are now positioned correctly on cropped PDFs and appear in every view mode — including the continuous scroll, two-page, and grid views, not just single-page view.",
                "New Transform Pages tool (Tools menu): rotate pages in 90° steps, fine-deskew by a small angle, scale up or down, and flip horizontally or vertically — over the whole document or a page range. A plain 90° rotation stays crisp and text-selectable; deskew, scale, and flip re-render the affected pages as images.",
                "New Watermark / Stamp tool (Tools menu): add a semi-transparent text watermark like \"CONFIDENTIAL\" — diagonal, tiled, or pinned to any corner — and optionally stamp an image or logo, with adjustable opacity, size, rotation, and a page range.",
                "New color picker for the Draw, Line, and Text tools: click the \"+\" next to the swatches to choose any color with an RGB/hex picker, or use the eyedropper to grab a color from anywhere on your screen.",
                "Recent files: reopen a recently-opened PDF from the start screen or by right-clicking the Open button; missing files clean themselves out of the list.",
                "OCR upgrades: pick from many languages and a high-quality mode, copy a page's recognized text to the clipboard, or extract all text to a .txt/.md file — all on your machine.",
                "New full-screen mode (F11) plus keyboard shortcuts: F1 shortcuts, F2 about, F5–F8 view modes, and letter keys (V/T/H/D/L/I) to pick Edit tools.",
                "New Document Info (Tools menu or F12): view and edit a PDF's title, author, subject, keywords, and creator, with a read-only summary of producer, page count, version, date, and size.",
                "New Line tool in the Edit tab: drag to draw a straight line, and hold Shift to snap it to horizontal, vertical, or 45°. It uses the same color, width, and opacity as the Draw tool.",
                "Fixed: editing existing Hebrew (and other right-to-left) text now keeps the words in their correct order — a line with more than one Hebrew word used to come back reversed in the edit box.",
                "Hebrew and Arabic edit boxes now read and align right-to-left while you type, so editing existing text feels natural.",
                "Editing existing text now matches the original font: if that font isn't installed, Scalpel uses the document's own embedded font when it can, and otherwise tells you exactly which font to install and uses a close substitute in the meantime — for any language.",
                "Fixed: Redact now works on PDFs with damaged or unusual internal structure that previously failed with an \"Unexpected token 'xref'\" error — such files are now safely flattened and redacted instead.",
                "Fixed: OCR (Make Searchable) now actually produces searchable text. Previously the recognized text could be silently dropped, leaving the saved PDF non-searchable; the page text is now selectable and searchable as intended.",
                "The Tools operations (Redact, Compress, Make Searchable) are also more robust: if something goes wrong they now show a clear message instead of closing the app.",
                "Compress now explains it works best on scanned or photo-heavy PDFs (mostly-text files may not shrink), and reuses an already-installed Tesseract for OCR instead of re-downloading language data.",
            }),
            new Release("1.8.0", "June 2026", new[]
            {
                "Optional update notifications: Scalpel can now let you know when a new version is out. It's off until you turn it on, sends no information about you or your files, and you can toggle it anytime in Settings.",
                "The notification links you straight to the right place to update — the Microsoft Store for Store installs, the website for portable and installed copies.",
                "Fixed: the Settings checkboxes are now clearly legible in Dark and High Contrast themes.",
            }),
            new Release("1.7.0", "June 2026", new[]
            {
                "Brand-new ribbon interface: the toolbar is now organized like familiar office apps — the View, Edit, Pages, and Sign tabs open clearly labeled groups of tools.",
                "A Quick Access toolbar in the title bar keeps Open, Save, Print, and Undo one click away from anywhere.",
                "Refreshed look: a clean, clinical light theme with a precise red accent is now the default, with a smooth animated transition when you switch tabs.",
                "Zoom moved to the bottom-right of the status bar, where you'd expect it.",
                "The new design carries across every theme — Light, Dark, and High Contrast — and all accent colors.",
                "Refreshed app icon and Store artwork to match — a steel tile with the scalpel's surgical-red cutting edge.",
            }),
            new Release("1.6.0", "June 2026", new[]
            {
                "New Tools menu with five local-only power features — no subscription, no uploads.",
                "Page numbering, Bates numbering, and custom headers/footers you can place in any corner.",
                "Compress PDF: shrink scan- and photo-heavy files with Low/Medium/High presets, all on your machine.",
                "Make Searchable (OCR): turn scanned pages into selectable, searchable text offline (one-time local language-data download).",
                "Password protect: encrypt a copy with a password and printing/copying permissions.",
                "Remove metadata: strip author, title, and hidden data before sharing.",
            }),
            new Release("1.5.1", "June 2026", new[]
            {
                "Added Hebrew, Arabic, and Russian — including a full right-to-left interface for Hebrew and Arabic.",
                "You can now type and edit Hebrew, Arabic, and Russian text directly on your PDFs; letters shape and read in the correct order.",
                "Settings now group Theme, Accent, and Language into collapsible sections for a tidier panel.",
                "Added a “What's New” button (next to About) with release notes organized by version.",
                "The toolbar and Settings panel scroll on small screens, so nothing gets cut off.",
                "Fixed: editing text while using a right-to-left interface now places the edit box over the correct spot.",
                "Fixed: the text edit box now matches the size of the text you're editing, including large headings.",
            }),
            new Release("1.5.0", "June 2026", new[]
            {
                "New “Studio” interface: a cleaner toolbar organized into View, Edit, Pages, and Sign modes.",
                "Light, Dark, and High-Contrast themes, each with Amber, Red, Green, and Cyan accent colors.",
                "More interface languages: Spanish, Traditional and Simplified Chinese, Bengali, and Turkish.",
                "Faster, sharper page rendering and a refreshed page thumbnail sidebar.",
            }),
            new Release("1.4", "Earlier", new[]
            {
                "Core editing toolkit: add text, highlights, freehand ink, images, and signatures.",
                "Merge, split, extract, reorder, rotate, and crop pages.",
                "Fill in PDF forms, print, and save a flattened (uneditable) copy.",
                "Single portable app with no installer and no telemetry.",
            }),
        };
    }
}
