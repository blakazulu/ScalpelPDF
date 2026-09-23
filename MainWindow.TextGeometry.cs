using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Text geometry: reading a page's words in the frame it is shown in
        // ============================================================

        /// <summary>A PdfPig page opened for reading word positions; dispose to close it.</summary>
        private sealed class DisplayedPage : IDisposable
        {
            private readonly PdfPigDoc _doc;
            public UglyToad.PdfPig.Content.Page Page { get; }
            public DisplayedPage(PdfPigDoc doc, UglyToad.PdfPig.Content.Page page) { _doc = doc; Page = page; }
            public void Dispose() => _doc.Dispose();
        }

        /// <summary>
        /// Opens page <paramref name="pageIdx"/> for PdfPig so its word boxes are in the frame the
        /// canvas shows: y-up, measured from the crop box, with the page's rotation applied, and
        /// <c>Page.Width/Height</c> the visible size. PdfPig does that itself for a page that still
        /// carries its /Rotate. After a page operation, though, Scalpel moves the rotation out of
        /// the working file into <see cref="_pageRotations"/> and rotates the bitmap instead, so
        /// PdfPig would read that page unrotated - words in the wrong place, and sideways text it
        /// splits into single letters. For such a page, a one-page copy with its rotation put back
        /// is read instead. Returns null when the page cannot be read.
        /// </summary>
        private DisplayedPage? OpenDisplayedPage(int pageIdx)
        {
            if (_currentFile is null || pageIdx < 0) return null;
            _pageRotations.TryGetValue(pageIdx, out int stripped);
            stripped = ((stripped % 360) + 360) % 360;

            if (stripped != 0)
            {
                try
                {
                    using var src = PdfReader.Open(_currentFile, PdfDocumentOpenMode.Import);
                    if (pageIdx < src.PageCount)
                    {
                        using var one = new PdfDocument();
                        var copy = one.AddPage(src.Pages[pageIdx]);
                        Scalpel.Services.PageRotation.Set(copy, stripped);
                        using var ms = new MemoryStream();
                        one.Save(ms);
                        var rotated = PdfPigDoc.Open(ms.ToArray());
                        return new DisplayedPage(rotated, rotated.GetPage(1));
                    }
                }
                catch { /* fall through to the working file: better unrotated than nothing */ }
            }

            var pig = PdfPigDoc.Open(_currentFile);
            if (pageIdx >= pig.NumberOfPages) { pig.Dispose(); return null; }
            return new DisplayedPage(pig, pig.GetPage(pageIdx + 1));
        }

        /// <summary>A word's box in canvas space. PdfPig's corners can come back swapped for
        /// rotated text, so the box is normalized from all four.</summary>
        private static System.Windows.Rect WordCanvasRect(UglyToad.PdfPig.Core.PdfRectangle bb,
            double pdfW, double pdfH, double renderW, double renderH)
        {
            double x1 = Math.Min(Math.Min(bb.TopLeft.X, bb.TopRight.X), Math.Min(bb.BottomLeft.X, bb.BottomRight.X));
            double x2 = Math.Max(Math.Max(bb.TopLeft.X, bb.TopRight.X), Math.Max(bb.BottomLeft.X, bb.BottomRight.X));
            double y1 = Math.Min(Math.Min(bb.TopLeft.Y, bb.TopRight.Y), Math.Min(bb.BottomLeft.Y, bb.BottomRight.Y));
            double y2 = Math.Max(Math.Max(bb.TopLeft.Y, bb.TopRight.Y), Math.Max(bb.BottomLeft.Y, bb.BottomRight.Y));
            var r = Scalpel.Services.PageSpaceMap.ToCanvas(x1, y1, x2, y2, pdfW, pdfH, renderW, renderH, 0);
            return new System.Windows.Rect(r.X, r.Y, r.W, r.H);
        }
    }
}
