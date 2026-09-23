using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class ViewerRenderResolutionTests
{
    [Theory]
    [InlineData(1.0, 1.0, 1.0, 2048)]
    [InlineData(1.5, 1.5, 1.0, 3072)]
    [InlineData(1.0, 1.0, 2.0, 4096)]
    [InlineData(2.0, 2.0, 2.0, 6144)]
    public void TwoPageSecondaryMatchesPrimary(
        double dpiX, double dpiY, double zoom, int expected)
    {
        Assert.Equal(expected, ViewerRenderResolution.Primary(dpiX, dpiY, zoom));
        Assert.Equal(expected, ViewerRenderResolution.Secondary(true, dpiX, dpiY, zoom));
    }

    [Fact]
    public void GridSecondaryRetainsMemoryLimitedBudget()
    {
        Assert.Equal(1536, ViewerRenderResolution.Secondary(false, 1.0, 1.0, 4.0));
        Assert.Equal(3072, ViewerRenderResolution.Secondary(false, 2.0, 2.0, 4.0));
    }
}
