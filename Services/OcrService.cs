using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    /// <summary>A single OCR-recognized word with its bounding box in PDF points (top-left origin).</summary>
    public sealed class OcrWord
    {
        public string Text { get; set; } = "";
        public double XPt { get; set; }
        public double YPt { get; set; }
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
    }

    /// <summary>OCR result for one page.</summary>
    public sealed class OcrPageResult
    {
        public List<OcrWord> Words { get; set; } = new();
    }

    /// <summary>
    /// Recognizes text in a raster image. Implemented in the app over a local Tesseract engine;
    /// tests provide a fake so the searchable-layer logic runs without native code or training data.
    /// </summary>
    public interface IOcrEngine
    {
        OcrPageResult Recognize(byte[] imageBytes, double pageWidthPt, double pageHeightPt);
    }

    /// <summary>One page to write: the raster image, its point size, and its OCR result.</summary>
    public sealed class OcrPage
    {
        public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
        public OcrPageResult Ocr { get; set; } = new();
    }

    /// <summary>
    /// Builds a searchable PDF from page rasters + OCR results: each page is the original image with
    /// an invisible text layer (drawn with a fully-transparent brush) positioned over each word, so
    /// the text is selectable/searchable but does not alter the page's appearance.
    /// </summary>
    public static class SearchableLayerWriter
    {
        // Fully transparent — present in the content stream (extractable) but not painted.
        private static readonly XBrush Invisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

        public static void Write(IReadOnlyList<OcrPage> pages, string outputPath)
        {
            if (pages is null) throw new ArgumentNullException(nameof(pages));

            using var doc = new PdfDocument();
            foreach (var p in pages)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(p.WidthPt);
                page.Height = XUnit.FromPoint(p.HeightPt);

                using var gfx = XGraphics.FromPdfPage(page);
                byte[] copy = (byte[])p.ImageBytes.Clone();
                var ximg = XImage.FromStream(() => new MemoryStream(copy));
                gfx.DrawImage(ximg, 0, 0, p.WidthPt, p.HeightPt);

                foreach (var w in p.Ocr.Words)
                {
                    if (string.IsNullOrEmpty(w.Text)) continue;
                    double fontSize = Math.Max(1, w.HeightPt);
                    double baselineY = w.YPt + w.HeightPt; // box top + height ≈ baseline
                    // Use Geist (bundled) — ASCII OCR text extracts reliably.
                    var font = new XFont("Geist", fontSize, XFontStyle.Regular);
                    gfx.DrawString(w.Text, font, Invisible, new XPoint(w.XPt, baselineY));
                }
            }
            doc.Save(outputPath);
        }
    }

    /// <summary>
    /// Orchestrates OCR: rasterizes each page (via <see cref="IPageRasterizer"/>), runs the supplied
    /// <see cref="IOcrEngine"/>, and writes a searchable PDF. Fully native-free given fakes.
    /// </summary>
    public static class OcrService
    {
        public static void MakeSearchable(IPageRasterizer source, IOcrEngine engine, string outputPath)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (engine is null) throw new ArgumentNullException(nameof(engine));

            var pages = new List<OcrPage>();
            for (int i = 0; i < source.PageCount; i++)
            {
                var raster = source.RenderPage(i);
                var (wPt, hPt) = source.PageSizePt(i);
                var ocr = engine.Recognize(raster.ImageBytes, wPt, hPt);
                pages.Add(new OcrPage { ImageBytes = raster.ImageBytes, WidthPt = wPt, HeightPt = hPt, Ocr = ocr });
            }
            SearchableLayerWriter.Write(pages, outputPath);
        }
    }
}
