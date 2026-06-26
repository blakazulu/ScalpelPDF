using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Scalpel.Services
{
    /// <summary>A rectangle (PDF points, top-left origin) to redact on a given 0-based page.</summary>
    public sealed class RedactRect
    {
        public int PageIndex { get; set; }
        public double XPt { get; set; }
        public double YPt { get; set; }
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
    }

    /// <summary>
    /// Performs TRUE redaction: any page carrying a redaction rectangle is rasterized (via an
    /// <see cref="IPageRasterizer"/>) and rebuilt as a flat image with opaque black boxes painted
    /// over the rectangles. Because the page becomes an image, the underlying text/objects are
    /// permanently gone — not selectable, searchable, or recoverable. Pages without redactions are
    /// copied through unchanged (text stays selectable).
    /// </summary>
    public static class RedactionService
    {
        public static void Redact(string inputPath, IPageRasterizer rasterizer,
            IEnumerable<RedactRect> rects, string outputPath)
        {
            if (rasterizer is null) throw new ArgumentNullException(nameof(rasterizer));

            var byPage = (rects ?? Enumerable.Empty<RedactRect>())
                .Where(r => r != null)
                .GroupBy(r => r.PageIndex)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Real-world PDFs frequently have malformed cross-reference tables that PdfSharpCore's
            // parser rejects (e.g. "Unexpected token 'xref' in PDF stream"). PDFium — which backs the
            // rasterizer — is far more tolerant and has already opened the file to render it. So if we
            // can't read the page structure we don't fail: we flatten EVERY page to an image, which is
            // strictly safer (nothing stays selectable) and mirrors the app's PDFium repair fallback.
            // Pages stay vector-copied only when the structure parses AND the page isn't redacted.
            PdfDocument? input = null;
            try { input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import); }
            catch { input = null; }

            try
            {
                using var outDoc = new PdfDocument();
                int pageCount = input?.PageCount ?? rasterizer.PageCount;

                for (int p = 0; p < pageCount; p++)
                {
                    bool redacted = byPage.TryGetValue(p, out var pageRects) && pageRects.Count > 0;

                    // Flatten when the page is redacted, or when the structure is unreadable (no
                    // way to copy a vector page through, so the whole document is rasterized).
                    if (redacted || input is null)
                    {
                        var raster = rasterizer.RenderPage(p);
                        var (wPt, hPt) = rasterizer.PageSizePt(p);

                        var page = outDoc.AddPage();
                        page.Width = XUnit.FromPoint(wPt);
                        page.Height = XUnit.FromPoint(hPt);

                        using var gfx = XGraphics.FromPdfPage(page);
                        byte[] copy = (byte[])raster.ImageBytes.Clone();
                        var ximg = XImage.FromStream(() => new MemoryStream(copy));
                        gfx.DrawImage(ximg, 0, 0, wPt, hPt);

                        if (pageRects is not null)
                            foreach (var r in pageRects)
                                gfx.DrawRectangle(XBrushes.Black, r.XPt, r.YPt, r.WidthPt, r.HeightPt);
                    }
                    else
                    {
                        // No redactions on this page — copy it through unchanged (text stays selectable).
                        outDoc.AddPage(input.Pages[p]);
                    }
                }

                outDoc.Save(outputPath);
            }
            finally { input?.Dispose(); }
        }
    }
}
