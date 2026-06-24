using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Scalpel.Services
{
    /// <summary>A rendered page raster: encoded image bytes plus its pixel dimensions.</summary>
    public sealed class RasterPage
    {
        public RasterPage(byte[] imageBytes, int pixelWidth, int pixelHeight)
        {
            ImageBytes = imageBytes;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }
        public byte[] ImageBytes { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
    }

    /// <summary>
    /// Renders PDF pages to raster images. The app implements this over Docnet/PDFium;
    /// tests provide a fake so the compression/redaction logic is exercised without native code.
    /// </summary>
    public interface IPageRasterizer
    {
        int PageCount { get; }
        (double widthPt, double heightPt) PageSizePt(int pageIndex);
        RasterPage RenderPage(int pageIndex);
    }

    /// <summary>
    /// Image-and-PDF primitives shared by compression and redaction: re-encode an image as
    /// JPEG (optionally downscaled), and append an image as a full-bleed PDF page.
    /// Uses SixLabors.ImageSharp (already in the dependency tree via PdfSharpCore).
    /// </summary>
    public static class PdfRasterTools
    {
        /// <summary>
        /// Re-encodes <paramref name="source"/> (any decodable image) as a JPEG at the given
        /// quality (1–100). If <paramref name="maxDimension"/> &gt; 0 and the image's longest
        /// side exceeds it, the image is proportionally downscaled first.
        /// </summary>
        public static byte[] ReencodeJpeg(byte[] source, int quality, int maxDimension = 0)
        {
            if (source is null || source.Length == 0) throw new ArgumentException("empty image", nameof(source));
            quality = Math.Max(1, Math.Min(100, quality));

            using var image = Image.Load(source);
            if (maxDimension > 0)
            {
                int longest = Math.Max(image.Width, image.Height);
                if (longest > maxDimension)
                {
                    double scale = (double)maxDimension / longest;
                    int w = Math.Max(1, (int)Math.Round(image.Width * scale));
                    int h = Math.Max(1, (int)Math.Round(image.Height * scale));
                    image.Mutate(x => x.Resize(w, h));
                }
            }

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
            return ms.ToArray();
        }

        /// <summary>Appends a new page sized to <paramref name="widthPt"/> x <paramref name="heightPt"/>
        /// (PDF points) filled edge-to-edge with the given image.</summary>
        public static void AppendImagePage(PdfDocument doc, byte[] imageBytes, double widthPt, double heightPt)
        {
            if (doc is null) throw new ArgumentNullException(nameof(doc));
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(widthPt);
            page.Height = XUnit.FromPoint(heightPt);
            using var gfx = XGraphics.FromPdfPage(page);
            // copy so the closure owns an independent stream each time PdfSharpCore reads it
            byte[] copy = (byte[])imageBytes.Clone();
            var xImg = XImage.FromStream(() => new MemoryStream(copy));
            gfx.DrawImage(xImg, 0, 0, widthPt, heightPt);
        }
    }
}
