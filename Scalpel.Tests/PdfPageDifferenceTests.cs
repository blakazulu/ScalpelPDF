using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class PdfPageDifferenceTests
{
    [Fact]
    public void IdenticalPagesHaveNoDifferences()
    {
        byte[] pixels = WhitePage(24, 24);
        PageDifferenceResult result = PdfPageDifference.Compare(pixels, 24, 24, pixels, 24, 24);

        Assert.False(result.IsDifferent);
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void SmallColorNoiseIsIgnored()
    {
        byte[] left = WhitePage(24, 24);
        byte[] right = (byte[])left.Clone();
        right[0] -= 10;

        Assert.False(PdfPageDifference.Compare(left, 24, 24, right, 24, 24).IsDifferent);
    }

    [Fact]
    public void NeighboringChangedTilesBecomeOneRegion()
    {
        byte[] left = WhitePage(36, 24);
        byte[] right = (byte[])left.Clone();
        Paint(right, 36, 8, 5, 18, 8);

        PageDifferenceResult result = PdfPageDifference.Compare(left, 36, 24, right, 36, 24);

        Assert.True(result.IsDifferent);
        Assert.Single(result.Regions);
        Assert.True(result.Regions[0].ChangedPixels >= 100);
    }

    [Fact]
    public void DifferentPageDimensionsAreReportedExplicitly()
    {
        PageDifferenceResult result = PdfPageDifference.Compare(
            WhitePage(12, 12), 12, 12, WhitePage(24, 12), 24, 12);

        Assert.True(result.IsDifferent);
        Assert.False(result.DimensionsMatch);
        Assert.Empty(result.Regions);
    }

    private static byte[] WhitePage(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;
        return pixels;
    }

    private static void Paint(byte[] pixels, int width, int x, int y, int w, int h)
    {
        for (int py = y; py < y + h; py++)
        for (int px = x; px < x + w; px++)
        {
            int offset = (py * width + px) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
        }
    }
}
