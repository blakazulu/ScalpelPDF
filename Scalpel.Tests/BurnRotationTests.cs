using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// The page is 600x800 points. The canvas the user annotated is the page as displayed, so for
    /// a quarter turn it is landscape: 1000x750 pixels. Each test maps the canvas corners and
    /// checks they land on the corresponding corners of the unrotated page.
    /// </summary>
    public class BurnRotationTests
    {
        private const double PageW = 600, PageH = 800;

        // Canvas showing an unrotated page (portrait) and a quarter-turned one (landscape).
        private const double PortraitCanvasW = 750, PortraitCanvasH = 1000;
        private const double LandscapeCanvasW = 1000, LandscapeCanvasH = 750;

        private static void AssertPoint((double X, double Y) actual, double x, double y)
        {
            Assert.Equal(x, actual.X, 3);
            Assert.Equal(y, actual.Y, 3);
        }

        [Fact]
        public void NoRotationIsAPlainScale()
        {
            var t = BurnRotation.For(0, PageW, PageH, PortraitCanvasW, PortraitCanvasH);
            Assert.True(t.IsAxisAligned);
            AssertPoint(t.Apply(0, 0), 0, 0);
            AssertPoint(t.Apply(PortraitCanvasW, PortraitCanvasH), PageW, PageH);
            AssertPoint(t.Apply(PortraitCanvasW / 2, PortraitCanvasH / 2), PageW / 2, PageH / 2);
        }

        [Fact]
        public void QuarterTurnMapsCanvasCornersOntoTheUnrotatedPage()
        {
            // Surface is the unrotated page (Scalpel stripped /Rotate into its own map).
            var t = BurnRotation.For(90, PageW, PageH, LandscapeCanvasW, LandscapeCanvasH);
            Assert.False(t.IsAxisAligned);

            // Rotating the page 90 degrees clockwise for display puts the page's top-left corner
            // at the visual top-RIGHT, so the canvas top-left must map to the page's bottom-left.
            AssertPoint(t.Apply(0, 0), 0, PageH);
            AssertPoint(t.Apply(LandscapeCanvasW, 0), 0, 0);                       // canvas top-right -> page top-left
            AssertPoint(t.Apply(LandscapeCanvasW, LandscapeCanvasH), PageW, 0);    // canvas bottom-right -> page top-right
            AssertPoint(t.Apply(0, LandscapeCanvasH), PageW, PageH);               // canvas bottom-left -> page bottom-right
        }

        [Fact]
        public void ThreeQuarterTurnIsTheOppositeQuarterTurn()
        {
            var t = BurnRotation.For(270, PageW, PageH, LandscapeCanvasW, LandscapeCanvasH);
            Assert.False(t.IsAxisAligned);

            AssertPoint(t.Apply(0, 0), PageW, 0);                                  // canvas top-left -> page top-right
            AssertPoint(t.Apply(LandscapeCanvasW, 0), PageW, PageH);
            AssertPoint(t.Apply(LandscapeCanvasW, LandscapeCanvasH), 0, PageH);
            AssertPoint(t.Apply(0, LandscapeCanvasH), 0, 0);
        }

        [Fact]
        public void HalfTurnFlipsBothAxes()
        {
            var t = BurnRotation.For(180, PageW, PageH, PortraitCanvasW, PortraitCanvasH);
            Assert.True(t.IsAxisAligned);   // 180 is a flip, not a transpose

            AssertPoint(t.Apply(0, 0), PageW, PageH);
            AssertPoint(t.Apply(PortraitCanvasW, PortraitCanvasH), 0, 0);
            AssertPoint(t.Apply(PortraitCanvasW / 2, PortraitCanvasH / 2), PageW / 2, PageH / 2);
        }

        [Fact]
        public void QuarterTurnKeepsTheCentreAtTheCentre()
        {
            foreach (int rot in new[] { 90, 270 })
            {
                var t = BurnRotation.For(rot, PageW, PageH, LandscapeCanvasW, LandscapeCanvasH);
                AssertPoint(t.Apply(LandscapeCanvasW / 2, LandscapeCanvasH / 2), PageW / 2, PageH / 2);
            }
        }

        [Fact]
        public void ASurfaceThatIsAlreadyRotatedNeedsOnlyAScale()
        {
            // A page that kept its own /Rotate: PDFium and XGraphics both work in the rotated
            // frame, so the surface is landscape like the canvas and no rotation must be applied.
            var t = BurnRotation.For(90, PageH, PageW, LandscapeCanvasW, LandscapeCanvasH);
            Assert.True(t.IsAxisAligned);
            AssertPoint(t.Apply(0, 0), 0, 0);
            AssertPoint(t.Apply(LandscapeCanvasW, LandscapeCanvasH), PageH, PageW);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void EveryRotationKeepsPointsInsideThePage(int rot)
        {
            double cw = rot is 90 or 270 ? LandscapeCanvasW : PortraitCanvasW;
            double ch = rot is 90 or 270 ? LandscapeCanvasH : PortraitCanvasH;
            var t = BurnRotation.For(rot, PageW, PageH, cw, ch);

            foreach (var (x, y) in new[] { (0.0, 0.0), (cw, 0.0), (0.0, ch), (cw, ch), (cw / 3, ch / 7) })
            {
                var p = t.Apply(x, y);
                Assert.InRange(p.X, -0.001, PageW + 0.001);
                Assert.InRange(p.Y, -0.001, PageH + 0.001);
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(600, 0)]
        [InlineData(0, 800)]
        public void DegenerateInputsFallBackToIdentity(double renderW, double renderH)
        {
            var t = BurnRotation.For(90, PageW, PageH, renderW, renderH);
            Assert.Equal(BurnRotation.Transform.Identity, t);
        }
    }
}
