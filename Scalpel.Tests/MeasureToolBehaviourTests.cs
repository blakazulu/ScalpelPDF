using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// The property that makes a measure tool trustworthy: the reading depends only on the page,
    /// never on how far the user has zoomed in or what resolution the page happened to render at.
    /// </summary>
    public class MeasureToolBehaviourTests
    {
        // US Letter.
        private const double PageW = 612, PageH = 792;

        [Fact]
        public void TheSameSpanReadsTheSameAtAnyRenderResolution()
        {
            // Half the page width, measured on a small render and on a large one.
            var low = MeasurementCalculator.Calculate(PageW, PageH, 0, 600, 776, 300, 0);
            var high = MeasurementCalculator.Calculate(PageW, PageH, 0, 2400, 3104, 1200, 0);

            Assert.Equal(low.Points, high.Points, 6);
            Assert.Equal(PageW / 2, low.Points, 6);
        }

        [Fact]
        public void AFullPageWidthMeasuresTheRealPageWidth()
        {
            var m = MeasurementCalculator.Calculate(PageW, PageH, 0, 1000, 1294, 1000, 0);
            Assert.Equal(PageW, m.Points, 6);
            Assert.Equal(PageW / 72.0, m.Inches, 6);
            Assert.Equal(PageW / 72.0 * 25.4, m.Millimetres, 4);
        }

        [Theory]
        [InlineData(90)]
        [InlineData(270)]
        public void AQuarterTurnSwapsThePageAxes(int rotation)
        {
            // Displayed landscape, so a full-width drag spans the page's HEIGHT.
            var m = MeasurementCalculator.Calculate(PageW, PageH, rotation, 1000, 773, 1000, 0);
            Assert.Equal(PageH, m.Points, 6);
        }

        [Fact]
        public void HalfATurnLeavesTheAxesAlone()
        {
            var m = MeasurementCalculator.Calculate(PageW, PageH, 180, 1000, 1294, 1000, 0);
            Assert.Equal(PageW, m.Points, 6);
        }

        [Fact]
        public void ADiagonalUsesBothAxes()
        {
            // A 3-4-5 triangle in page points: the reading must be the hypotenuse.
            var m = MeasurementCalculator.Calculate(720, 720, 0, 720, 720, 300, 400);
            Assert.Equal(500, m.Points, 6);
        }

        [Fact]
        public void AZeroLengthDragMeasuresNothing()
        {
            var m = MeasurementCalculator.Calculate(PageW, PageH, 0, 1000, 1294, 0, 0);
            Assert.Equal(0, m.Points, 9);
        }

        [Fact]
        public void DirectionDoesNotChangeTheDistance()
        {
            var forward = MeasurementCalculator.Calculate(PageW, PageH, 0, 1000, 1294, 250, 120);
            var back = MeasurementCalculator.Calculate(PageW, PageH, 0, 1000, 1294, -250, -120);
            Assert.Equal(forward.Points, back.Points, 9);
        }

        [Fact]
        public void TheThreeUnitsAgreeWithEachOther()
        {
            var m = MeasurementCalculator.Calculate(PageW, PageH, 0, 1000, 1294, 617, 0);
            Assert.Equal(m.Points / 72.0, m.Inches, 9);
            Assert.Equal(m.Inches * 25.4, m.Millimetres, 9);
        }
    }
}
