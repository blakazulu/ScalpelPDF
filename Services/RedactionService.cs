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

            using var input = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var outDoc = new PdfDocument();

            for (int p = 0; p < input.PageCount; p++)
            {
                if (byPage.TryGetValue(p, out var pageRects) && pageRects.Count > 0)
                {
                    // Flatten this page to an image so all underlying text/objects are destroyed,
                    // then paint opaque black boxes over the redaction rectangles.
                    var raster = rasterizer.RenderPage(p);
                    var (wPt, hPt) = rasterizer.PageSizePt(p);

                    var page = outDoc.AddPage();
                    page.Width = XUnit.FromPoint(wPt);
                    page.Height = XUnit.FromPoint(hPt);

                    using var gfx = XGraphics.FromPdfPage(page);
                    byte[] copy = (byte[])raster.ImageBytes.Clone();
                    var ximg = XImage.FromStream(() => new MemoryStream(copy));
                    gfx.DrawImage(ximg, 0, 0, wPt, hPt);

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
    }
}
