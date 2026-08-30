using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>
    /// Pre-save fix-ups for PdfSharpCore documents (WPF-free).
    ///
    /// PdfSharpCore defect: merely reading <c>doc.Outlines</c> on a document that has no
    /// bookmarks creates an empty outline object and writes <c>/Outlines N 0 R</c> into the
    /// Catalog, but on save it never serialises object N - the xref entry for N ends up pointing
    /// at the xref table itself. Every PDF viewer with error recovery (PDFium, Acrobat) shrugs;
    /// PdfSharpCore's own parser throws "Unexpected token 'xref'" in every open mode. Scalpel
    /// read <c>Outlines</c> on every open (bookmarks sidebar), so every bookmark-less PDF it saved
    /// could not be re-opened or re-saved by Scalpel itself ("Save failed" after a successful
    /// write, "damaged structure" prompts on reopen).
    /// </summary>
    public static class PdfSaveGuard
    {
        private const string OutlinesKey = "/Outlines";

        /// <summary>
        /// Remove a dangling empty <c>/Outlines</c> reference from the Catalog. Real bookmarks
        /// are left untouched. Returns true if something was removed. Never throws.
        /// </summary>
        public static bool PrepareForSave(PdfDocument doc)
        {
            try
            {
                var catalog = doc.Internals.Catalog.Elements;
                if (!catalog.ContainsKey(OutlinesKey)) return false;
                if (doc.Outlines.Count > 0) return false;
                catalog.Remove(OutlinesKey);
                return true;
            }
            catch { return false; }
        }
    }
}
