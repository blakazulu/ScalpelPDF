using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// The canvas-to-PDF mapping used by the crop box, form widgets and link hotspots.
    ///
    /// <para>These expectations are derived from the physical page, not from either of the
    /// implementations that used to hand-roll this: take a portrait sheet and turn it, and see
    /// which edge ends up at the top. That is the only way to catch a transform where both the
    /// forward and inverse copies are wrong in the same direction, which is exactly how the 180
    /// degree crop bug survived - the round trip agreed with itself while the saved CropBox kept
    /// the mirrored half of the page.</para>
    /// </summary>
    public class PageSpaceMapTests
    {
        // A portrait page. At 90/270 the canvas is the same sheet on its side.
        private const double PdfW = 400, PdfH = 600;

        private static (double w, double h) Canvas(int rot)
            => rot is 90 or 270 ? (600, 400) : (400, 600);

        /// <summary>The PDF rect for the top half of what the user is looking at.</summary>
        private static (double X1, double Y1, double X2, double Y2) TopHalf(int rot)
        {
            var (cw, ch) = Canvas(rot);
            return PageSpaceMap.ToPdf(0, 0, cw, ch / 2, PdfW, PdfH, cw, ch, rot);
        }

        /// <summary>The PDF rect for the left half of what the user is looking at.</summary>
        private static (double X1, double Y1, double X2, double Y2) LeftHalf(int rot)
        {
            var (cw, ch) = Canvas(rot);
            return PageSpaceMap.ToPdf(0, 0, cw / 2, ch, PdfW, PdfH, cw, ch, rot);
        }

        [Fact]
        public void Unrotated_TopOfScreenIsTopOfPage()
        {
            var r = TopHalf(0);
            Assert.Equal(300, r.Y1, 3);
            Assert.Equal(600, r.Y2, 3);
        }

        [Fact]
        public void UpsideDown_TopOfScreenIsBottomOfPage()
        {
            // The regression. A page at 180 shows its bottom edge at the top of the screen, so
            // selecting the top half must keep PDF y in [0,300]. The old code returned [300,600]
            // and cropped the wrong half.
            var r = TopHalf(180);
            Assert.Equal(0, r.Y1, 3);
            Assert.Equal(300, r.Y2, 3);
        }

        [Fact]
        public void UpsideDown_LeftOfScreenIsRightOfPage()
        {
            var r = LeftHalf(180);
            Assert.Equal(200, r.X1, 3);
            Assert.Equal(400, r.X2, 3);
        }

        [Fact]
        public void QuarterTurnClockwise_TopOfScreenIsLeftEdgeOfPage()
        {
            // Turn a portrait sheet clockwise: its left edge swings up to the top.
            var r = TopHalf(90);
            Assert.Equal(0, r.X1, 3);
            Assert.Equal(200, r.X2, 3);
        }

        [Fact]
        public void QuarterTurnClockwise_LeftOfScreenIsBottomEdgeOfPage()
        {
            var r = LeftHalf(90);
            Assert.Equal(0, r.Y1, 3);
            Assert.Equal(300, r.Y2, 3);
        }

        [Fact]
        public void QuarterTurnCounterClockwise_TopOfScreenIsRightEdgeOfPage()
        {
            // Turn it the other way: the right edge swings up.
            var r = TopHalf(270);
            Assert.Equal(200, r.X1, 3);
            Assert.Equal(400, r.X2, 3);
        }

        [Fact]
        public void QuarterTurnCounterClockwise_LeftOfScreenIsTopEdgeOfPage()
        {
            var r = LeftHalf(270);
            Assert.Equal(300, r.Y1, 3);
            Assert.Equal(600, r.Y2, 3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void TheWholeCanvasIsTheWholePage(int rot)
        {
            var (cw, ch) = Canvas(rot);
            var r = PageSpaceMap.ToPdf(0, 0, cw, ch, PdfW, PdfH, cw, ch, rot);

            Assert.Equal(0, r.X1, 3);
            Assert.Equal(0, r.Y1, 3);
            Assert.Equal(PdfW, r.X2, 3);
            Assert.Equal(PdfH, r.Y2, 3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void CanvasToPdfAndBackIsTheSameRectangle(int rot)
        {
            var (cw, ch) = Canvas(rot);
            const double x = 37, y = 51, w = 120, h = 88;

            var pdf = PageSpaceMap.ToPdf(x, y, w, h, PdfW, PdfH, cw, ch, rot);
            var back = PageSpaceMap.ToCanvas(pdf.X1, pdf.Y1, pdf.X2, pdf.Y2, PdfW, PdfH, cw, ch, rot);

            Assert.Equal(x, back.X, 3);
            Assert.Equal(y, back.Y, 3);
            Assert.Equal(w, back.W, 3);
            Assert.Equal(h, back.H, 3);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void TheResultIsAlwaysNormalized(int rot)
        {
            var (cw, ch) = Canvas(rot);
            var r = PageSpaceMap.ToPdf(10, 20, 100, 60, PdfW, PdfH, cw, ch, rot);

            Assert.True(r.X1 <= r.X2, "x1 must not exceed x2");
            Assert.True(r.Y1 <= r.Y2, "y1 must not exceed y2");
        }

        [Fact]
        public void MatchesTheFormWidgetMappingAtEveryRotation()
        {
            // MainWindow.Forms.cs derived this transform independently and correctly. Pinning the
            // agreement keeps the two from drifting apart again.
            foreach (int rot in new[] { 0, 90, 180, 270 })
            {
                var (cw, ch) = Canvas(rot);
                double rx1 = 50, ry1 = 80, rx2 = 150, ry2 = 200;

                double ex, ey, ew, eh;
                switch (rot)
                {
                    case 90:
                        ex = ry1 / PdfH * cw; ey = rx1 / PdfW * ch;
                        ew = (ry2 - ry1) / PdfH * cw; eh = (rx2 - rx1) / PdfW * ch;
                        break;
                    case 180:
                        ex = (PdfW - rx2) / PdfW * cw; ey = ry1 / PdfH * ch;
                        ew = (rx2 - rx1) / PdfW * cw; eh = (ry2 - ry1) / PdfH * ch;
                        break;
                    case 270:
                        ex = (PdfH - ry2) / PdfH * cw; ey = (PdfW - rx2) / PdfW * ch;
                        ew = (ry2 - ry1) / PdfH * cw; eh = (rx2 - rx1) / PdfW * ch;
                        break;
                    default:
                        ex = rx1 / PdfW * cw; ey = (PdfH - ry2) / PdfH * ch;
                        ew = (rx2 - rx1) / PdfW * cw; eh = (ry2 - ry1) / PdfH * ch;
                        break;
                }

                var got = PageSpaceMap.ToCanvas(rx1, ry1, rx2, ry2, PdfW, PdfH, cw, ch, rot);
                Assert.Equal(ex, got.X, 3);
                Assert.Equal(ey, got.Y, 3);
                Assert.Equal(ew, got.W, 3);
                Assert.Equal(eh, got.H, 3);
            }
        }

        [Theory]
        [InlineData(-90, 270)]
        [InlineData(360, 0)]
        [InlineData(450, 90)]
        public void OutOfRangeRotationsAreNormalized(int given, int equivalent)
        {
            var (cw, ch) = Canvas(equivalent);
            var a = PageSpaceMap.ToPdf(10, 20, 50, 60, PdfW, PdfH, cw, ch, given);
            var b = PageSpaceMap.ToPdf(10, 20, 50, 60, PdfW, PdfH, cw, ch, equivalent);
            Assert.Equal(b, a);
        }

        [Fact]
        public void ADegenerateCanvasDoesNotDivideByZero()
        {
            var r = PageSpaceMap.ToPdf(0, 0, 10, 10, PdfW, PdfH, 0, 0, 0);
            Assert.False(double.IsNaN(r.X1) || double.IsInfinity(r.X1));
            Assert.False(double.IsNaN(r.Y1) || double.IsInfinity(r.Y1));
        }
    }
}
