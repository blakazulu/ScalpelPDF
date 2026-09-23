using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// End-to-end check of the rotation burn against a real PdfSharpCore page: applies the
    /// transform to an XGraphics exactly as MainWindow's burn does, draws at a known canvas point,
    /// then reads back with PdfPig where the glyph actually landed.
    ///
    /// <para>This is the scenario Scalpel is in after any page operation: the working document has
    /// /Rotate stripped to 0 and the real angle lives in the rotation map, so the annotation canvas
    /// is landscape while the page is still portrait.</para>
    /// </summary>
    public class BurnRotationIntegrationTests
    {
        private const double PageW = 600, PageH = 800;

        /// <summary>Burns "X" at a canvas point and returns where PdfPig finds it, in PDF space.</summary>
        private static (double X, double Y) BurnAndLocate(int rotation, double canvasW, double canvasH,
                                                          double canvasX, double canvasY)
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-burnrot-{Path.GetRandomFileName()}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(PageW);
                    page.Height = XUnit.FromPoint(PageH);
                    // Rotate deliberately left at 0: this mirrors Scalpel's stripped working copy.

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

                        var font = new XFont("Arial", 12 * sy, XFontStyle.Regular);
                        gfx.DrawString("X", font, XBrushes.Black,
                                       new XPoint(canvasX * sx, canvasY * sy));
                    }
                    doc.Save(path);
                }

                using var pig = UglyToad.PdfPig.PdfDocument.Open(path);
                var letters = pig.GetPage(1).Letters
                                 .Where(l => l.Value == "X")
                                 .ToList();
                Assert.True(letters.Count > 0, "burned glyph was not found in the saved PDF");
                var r = letters[0].GlyphRectangle;
                return (r.Left, r.Bottom);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void UnrotatedPageIsUnchangedByTheTransform()
        {
            // Canvas 750x1000 over a 600x800 page: 10% in, 10% down.
            var (x, y) = BurnAndLocate(0, 750, 1000, 75, 100);
            Assert.InRange(x, 55, 65);         // 10% of 600 = 60
            Assert.InRange(y, 690, 730);       // baseline at 800-80 = 720
        }

        [Fact]
        public void QuarterTurnPlacesTheGlyphOnTheCorrectEdgeOfThePage()
        {
            // Landscape canvas 1000x750 over the same portrait page, rotated 90 for display.
            // Canvas (100,75) is 10% across / 10% down the VISUAL page.
            // Visual point = (80, 60) of an 800x600 visual page.
            // Under a 90 degree clockwise display rotation that is page (60, 720) top-left origin,
            // i.e. PDF user space (60, 80).
            var (x, y) = BurnAndLocate(90, 1000, 750, 100, 75);
            Assert.InRange(x, 50, 70);
            Assert.InRange(y, 60, 100);
        }

        [Fact]
        public void ThreeQuarterTurnPlacesTheGlyphOnTheOppositeEdge()
        {
            // 270 is the mirror of 90: the same canvas point lands diagonally opposite.
            var (x, y) = BurnAndLocate(270, 1000, 750, 100, 75);
            Assert.InRange(x, 530, 550);       // 600 - 60
            Assert.InRange(y, 700, 740);       // 800 - 80
        }

        [Fact]
        public void HalfTurnFlipsBothAxes()
        {
            var (x, y) = BurnAndLocate(180, 750, 1000, 75, 100);
            // The draw origin mirrors to x = 600 - 60 = 540, but a half turn also flips the text
            // direction, so the glyph body extends LEFT of its origin - its reported left edge sits
            // one glyph width lower. Allow for that rather than pretending the origin is the edge.
            Assert.InRange(x, 528, 542);
            Assert.InRange(y, 70, 110);        // mirrored vertically
        }

        [Fact]
        public void QuarterTurnKeepsTheCentreGlyphAtThePageCentre()
        {
            var (x, y) = BurnAndLocate(90, 1000, 750, 500, 375);
            Assert.InRange(x, 285, 315);       // ~300 = PageW/2
            Assert.InRange(y, 385, 415);       // ~400 = PageH/2
        }
    }
}
