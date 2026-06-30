using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

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
        /// <summary>
        /// OCRs every page and writes a searchable PDF. <paramref name="onProgress"/> (if supplied)
        /// is invoked as (currentPage1Based, totalPages) before each page is recognized, and once
        /// more as (total, total) just before the file is written. <paramref name="cancel"/> aborts
        /// between pages by throwing <see cref="OperationCanceledException"/>.
        /// </summary>
        public static void MakeSearchable(IPageRasterizer source, IOcrEngine engine, string outputPath,
            Action<int, int>? onProgress = null,
            System.Threading.CancellationToken cancel = default)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (engine is null) throw new ArgumentNullException(nameof(engine));

            int total = source.PageCount;
            var pages = new List<OcrPage>();
            for (int i = 0; i < total; i++)
            {
                cancel.ThrowIfCancellationRequested();
                onProgress?.Invoke(i + 1, total);
                var raster = source.RenderPage(i);
                var (wPt, hPt) = source.PageSizePt(i);
                var ocr = engine.Recognize(raster.ImageBytes, wPt, hPt);
                pages.Add(new OcrPage { ImageBytes = raster.ImageBytes, WidthPt = wPt, HeightPt = hPt, Ocr = ocr });
            }
            cancel.ThrowIfCancellationRequested();
            onProgress?.Invoke(total, total);
            SearchableLayerWriter.Write(pages, outputPath);
        }

        /// <summary>
        /// OCRs a sub-region of one page and returns the recognized text (lines joined).
        /// The region is given as fractions (0..1) of the page in its native (unrotated) space with a
        /// top-left origin — exactly what <c>CanvasToPdfRect</c> produces once normalized. The page is
        /// rasterized, cropped to that rectangle, rotated by <paramref name="rotationDegrees"/>
        /// (0/90/180/270 — the user's display rotation) so text reaches the engine upright, then
        /// recognized. Pure given fakes: the crop/rotate/join logic runs without native code.
        /// </summary>
        public static string RecognizeRegionText(IPageRasterizer source, IOcrEngine engine, int pageIndex,
            double fracX, double fracY, double fracW, double fracH, int rotationDegrees = 0)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (engine is null) throw new ArgumentNullException(nameof(engine));

            var raster = source.RenderPage(pageIndex);
            var (wPt, hPt) = source.PageSizePt(pageIndex);

            // Clamp the fractional rect into the page and to a non-empty size.
            fracX = Math.Max(0, Math.Min(1, fracX));
            fracY = Math.Max(0, Math.Min(1, fracY));
            fracW = Math.Max(0, Math.Min(1 - fracX, fracW));
            fracH = Math.Max(0, Math.Min(1 - fracY, fracH));
            if (fracW <= 0 || fracH <= 0) return string.Empty;

            int px = (int)Math.Round(fracX * raster.PixelWidth);
            int py = (int)Math.Round(fracY * raster.PixelHeight);
            int pw = Math.Max(1, (int)Math.Round(fracW * raster.PixelWidth));
            int ph = Math.Max(1, (int)Math.Round(fracH * raster.PixelHeight));
            px = Math.Min(px, raster.PixelWidth - 1);
            py = Math.Min(py, raster.PixelHeight - 1);
            pw = Math.Min(pw, raster.PixelWidth - px);
            ph = Math.Min(ph, raster.PixelHeight - py);

            byte[] cropBytes;
            using (var image = Image.Load(raster.ImageBytes))
            {
                image.Mutate(c => c.Crop(new Rectangle(px, py, pw, ph)));
                var mode = (((rotationDegrees % 360) + 360) % 360) switch
                {
                    90 => RotateMode.Rotate90,
                    180 => RotateMode.Rotate180,
                    270 => RotateMode.Rotate270,
                    _ => RotateMode.None,
                };
                if (mode != RotateMode.None) image.Mutate(c => c.Rotate(mode));
                using var ms = new MemoryStream();
                image.Save(ms, new PngEncoder());
                cropBytes = ms.ToArray();
            }

            double regionWpt = fracW * wPt, regionHpt = fracH * hPt;
            if (rotationDegrees % 180 != 0) (regionWpt, regionHpt) = (regionHpt, regionWpt);
            var result = engine.Recognize(cropBytes, regionWpt, regionHpt);
            return OcrTextJoiner.Join(result.Words);
        }
    }
}
