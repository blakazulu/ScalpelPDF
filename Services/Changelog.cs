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
