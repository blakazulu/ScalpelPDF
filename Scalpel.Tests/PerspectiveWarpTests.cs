using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class PerspectiveWarpTests
{
    [Fact]
    public void IdentityCornersPreserveDimensionsAndCornerPixels()
    {
        byte[] pixels =
        [
            1,2,3,255,  4,5,6,255,
            7,8,9,255,  10,11,12,255,
        ];
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        Point[] corners = [new(0,0), new(1,0), new(1,1), new(0,1)];

        var result = PerspectiveWarp.Apply(source, corners);
        byte[] actual = new byte[16];
        result.CopyPixels(actual, 8, 0);

        Assert.Equal(2, result.PixelWidth);
        Assert.Equal(2, result.PixelHeight);
        Assert.Equal(pixels, actual);
    }

    [Fact]
    public void RejectsCollapsedQuadrilateral()
    {
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        Point[] corners = [new(0,0), new(0,0), new(0,0), new(0,0)];
        Assert.Throws<InvalidOperationException>(() => PerspectiveWarp.Apply(source, corners));
    }

    [Fact]
    public void IdentityDetectionMatchesTheUnitSquareOnly()
    {
        Assert.True(PerspectiveWarp.IsIdentity([new(0,0), new(1,0), new(1,1), new(0,1)]));
        Assert.False(PerspectiveWarp.IsIdentity([new(0.1,0), new(1,0), new(1,1), new(0,1)]));
        Assert.False(PerspectiveWarp.IsIdentity([new(0,0), new(1,0), new(1,1)]));
    }
}
