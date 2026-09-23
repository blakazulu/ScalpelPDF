using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class MeasurementToolTests
{
    [Fact]
    public void CalculatesDistanceInPdfUnitsIndependentOfRenderResolution()
    {
        var low = MeasurementCalculator.Calculate(612, 792, 0, 612, 792, 72, 0);
        var high = MeasurementCalculator.Calculate(612, 792, 0, 2448, 3168, 288, 0);

        Assert.Equal(72, low.Points, 8);
        Assert.Equal(1, low.Inches, 8);
        Assert.Equal(25.4, low.Millimetres, 8);
        Assert.Equal(low.Points, high.Points, 8);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void RotatedPagesUseDisplayedDimensions(int rotation)
    {
        var value = MeasurementCalculator.Calculate(612, 792, rotation, 792, 612, 792, 0);

        Assert.Equal(792, value.PageWidthPoints, 8);
        Assert.Equal(612, value.PageHeightPoints, 8);
        Assert.Equal(792, value.Points, 8);
    }

    [Fact]
    public void DiagonalMeasurementUsesBothPageAxes()
    {
        var value = MeasurementCalculator.Calculate(720, 720, 0, 1000, 1000, 300, 400);

        Assert.Equal(360, value.Points, 8);
        Assert.Equal(5, value.Inches, 8);
    }
}
