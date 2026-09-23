using System;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using SixLabors.ImageSharp;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Upstream KillerPDF issue #311 reports images coming out flipped vertically when burned in
    /// on save. That regression lives in their new engine burn; Scalpel still draws through
    /// PdfSharpCore's <c>XGraphics.DrawImage</c>, which handles PDF image space itself.
    ///
    /// <para>The invariant these tests pin is the one that actually matters to a user: an image
    /// placed a given way up on the page they are looking at must come out that way up in the
    /// saved file - at every page rotation. That guards both the #311 flip and the rotation burn's
    /// transform, which must rotate content without mirroring it.</para>
    /// </summary>
    // PDFium is a process-wide singleton, so every test class that drives it directly must
    // run serially - exactly the discipline PdfiumGate enforces in the app.
    [Collection("Pdfium")]
    public class BurnImageOrientationTests
    {
        private const double PageW = 400, PageH = 600;

        /// <summary>A 40x40 PNG: solid red top half, solid blue bottom half.</summary>
        private static byte[] RedOverBluePng()
        {
            using var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(40, 40);
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    var c = y < 20
                        ? new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0, 255)
                        : new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 255, 255);
                    for (int x = 0; x < row.Length; x++) row[x] = c;
                }
            });
            using var ms = new MemoryStream();
            ImageExtensions.SaveAsPng(img, ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Burns the test image over the whole annotation canvas through the same code shape the
        /// real burn uses, then rasterizes the page as a viewer would show it and samples the
        /// colour a given fraction down the visible page.
        /// </summary>
        private static (byte R, byte G, byte B) BurnAndSample(int rotation, double fx, double fy)
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-imgorient-{Path.GetRandomFileName()}.pdf");
            try
            {
                byte[] png = RedOverBluePng();
                // The canvas is the page as displayed, so a quarter turn makes it landscape.
                double canvasW = rotation is 90 or 270 ? 600 : 400;
                double canvasH = rotation is 90 or 270 ? 400 : 600;

                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(PageW);
                    page.Height = XUnit.FromPoint(PageH);

                    using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                    {
                        var burn = BurnRotation.For(rotation, gfx.PageSize.Width, gfx.PageSize.Height,
                                                    canvasW, canvasH);
                        double sx, sy;
                        if (burn.IsAxisAligned && burn.OffsetX == 0 && burn.OffsetY == 0)
                        {
                            sx = burn.M11; sy = burn.M22;
                        }
                        else
                        {
                            gfx.MultiplyTransform(new XMatrix(burn.M11, burn.M12, burn.M21, burn.M22,
                                                              burn.OffsetX, burn.OffsetY));
                            sx = 1.0; sy = 1.0;
                        }

                        // Exactly the call shape MainWindow's image burn uses.
                        var xi = XImage.FromStream(() => new MemoryStream(png));
                        gfx.DrawImage(xi, 0, 0, canvasW * sx, canvasH * sy);
                    }

                    // Give the page its real rotation so the rasterizer shows what a viewer shows.
                    page.Rotate = rotation;
                    doc.Save(path);
                }

                var (bgra, w, h) = Scalpel.Services.PdfiumGate.Run(() =>
                {
                    using var docReader = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(600, 600));
                    using var pr = docReader.GetPageReader(0);
                    return (pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover()),
                            pr.GetPageWidth(), pr.GetPageHeight());
                });

                int px = Math.Min(w - 1, Math.Max(0, (int)(fx * w)));
                int py = Math.Min(h - 1, Math.Max(0, (int)(fy * h)));
                int off = (py * w + px) * 4;
                return (bgra[off + 2], bgra[off + 1], bgra[off]);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void AssertRed((byte R, byte G, byte B) c)
            => Assert.True(c.R > 150 && c.B < 100, $"expected red, got ({c.R},{c.G},{c.B})");

        private static void AssertBlue((byte R, byte G, byte B) c)
            => Assert.True(c.B > 150 && c.R < 100, $"expected blue, got ({c.R},{c.G},{c.B})");

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void BurnedImageKeepsItsVisualOrientationAtEveryPageRotation(int rotation)
        {
            // The source image is red on top. Whatever the page rotation, the user placed it that
            // way up on the canvas, so that is how it must appear in the saved file.
            AssertRed(BurnAndSample(rotation, 0.5, 0.15));
            AssertBlue(BurnAndSample(rotation, 0.5, 0.85));
        }

        [Fact]
        public void ImageIsNotMirroredHorizontally()
        {
            // A mirrored transform would still pass the top/bottom check for this image, so pin
            // the horizontal axis too using the boundary row, which must sit at mid height.
            var mid = BurnAndSample(0, 0.5, 0.5);
            Assert.True(mid.R > 40 && mid.B > 40,
                $"expected the red/blue boundary at mid height, got ({mid.R},{mid.G},{mid.B})");
        }
    }
}
