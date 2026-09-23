using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class PrintPreviewColorTests
{
    [Fact]
    public void CreateGrayscaleBitmap_EqualizesColorChannelsAndPreservesAlpha()
    {
        byte[] sourcePixels =
        [
            0, 0, 255, 255,
            255, 128, 0, 73
        ];
        BitmapSource source = BitmapSource.Create(
            2, 1, 96, 96, PixelFormats.Bgra32, null, sourcePixels, 8);

        BitmapSource result = PrintColorConverter.CreateGrayscaleBitmap(source);
        byte[] pixels = new byte[8];
        result.CopyPixels(pixels, 8, 0);

        Assert.Equal(pixels[0], pixels[1]);
        Assert.Equal(pixels[1], pixels[2]);
        Assert.Equal(255, pixels[3]);
        Assert.Equal(pixels[4], pixels[5]);
        Assert.Equal(pixels[5], pixels[6]);
        Assert.Equal(73, pixels[7]);
        Assert.True(result.IsFrozen);
    }
}
