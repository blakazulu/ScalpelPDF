using System;
using PdfSharpCore.Pdf;

namespace Scalpel.Services
{
    public sealed class CompressionOptions
    {
        /// <summary>JPEG quality 1–100 for re-encoded page rasters. Lower = smaller file.</summary>
        public int JpegQuality { get; set; } = 50;
        /// <summary>If &gt; 0, the longest side of each page raster is clamped to this many pixels.</summary>
        public int MaxImageDimension { get; set; } = 0;

        public static CompressionOptions Low => new() { JpegQuality = 75, MaxImageDimension = 2200 };
        public static CompressionOptions Medium => new() { JpegQuality = 50, MaxImageDimension = 1700 };
        public static CompressionOptions High => new() { JpegQuality = 30, MaxImageDimension = 1200 };
    }

    /// <summary>
    /// Compresses a PDF by rasterizing each page (via an <see cref="IPageRasterizer"/>) and
    /// rebuilding it as a downscaled JPEG image. Highly effective on scan/photo-heavy PDFs.
    /// The page rasterization is supplied by the caller (Docnet/PDFium in the app), keeping
    /// this logic native-free and unit-testable. Note: output pages are images (text becomes
    /// non-selectable) — this is the trade-off for guaranteed size reduction.
    /// </summary>
    public static class PdfCompressionService
    {
        public static void Compress(IPageRasterizer source, CompressionOptions opts, string outputPath)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (opts is null) throw new ArgumentNullException(nameof(opts));

            using var doc = new PdfDocument();
            for (int i = 0; i < source.PageCount; i++)
            {
                var raster = source.RenderPage(i);
                byte[] jpeg = PdfRasterTools.ReencodeJpeg(raster.ImageBytes, opts.JpegQuality, opts.MaxImageDimension);
                var (wPt, hPt) = source.PageSizePt(i);
                PdfRasterTools.AppendImagePage(doc, jpeg, wPt, hPt);
            }
            doc.Save(outputPath);
        }
    }
}
