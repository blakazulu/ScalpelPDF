using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

/// <summary>
/// Recolouring a rendered page to grayscale or black and white.
///
/// <para>Two things are being pinned: the colour maths, and the storage format. Writing every mode
/// as 32-bit BGRA stores three identical channels for a gray page and spends 32 bits per pixel
/// carrying one bit for a bilevel one, so an exported page came out several times larger than it
/// needed to be. The compact formats have no alpha channel, so they are used only when the image
/// is fully opaque - which a rendered PDF page always is.</para>
/// </summary>
public sealed class TransformQualityTests
{
    /// <summary>A fully opaque BGRA image, the shape a rendered page always has.</summary>
    private static BitmapSource Opaque(int width, params byte[] grays)
    {
        var pixels = new byte[grays.Length * 4];
        for (int i = 0; i < grays.Length; i++)
        {
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = grays[i];
            pixels[i * 4 + 3] = 255;
        }
        return BitmapSource.Create(width, grays.Length / width, 96, 96,
                                   PixelFormats.Bgra32, null, pixels, width * 4);
    }

    [Fact]
    public void Grayscale_UsesPerceptualLuminance()
    {
        // Pure red: luminance is far below the mid-point, which a naive average would miss.
        BitmapSource source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
            null, new byte[] { 0, 0, 255, 255 }, 4);

        BitmapSource result = PageQualityConverter.ApplyColorMode(
            source, PageColorMode.Grayscale, 160);

        var pixel = new byte[1];
        result.CopyPixels(pixel, 1, 0);
        Assert.InRange(pixel[0], 75, 77);
    }

    [Fact]
    public void Grayscale_OfAnOpaquePageIsStoredAsOneChannel()
    {
        BitmapSource result = PageQualityConverter.ApplyColorMode(
            Opaque(2, 10, 200), PageColorMode.Grayscale, 160);

        Assert.Equal(PixelFormats.Gray8, result.Format);
        Assert.Equal(8, result.Format.BitsPerPixel);
    }

    [Fact]
    public void BlackAndWhite_UsesRequestedThreshold()
    {
        // 100 is below the threshold and 180 above it, so the row is black then white.
        BitmapSource result = PageQualityConverter.ApplyColorMode(
            Opaque(2, 100, 180), PageColorMode.BlackAndWhite, 160);

        // Read it back through a common format so the assertion is about colour, not packing.
        var asBgra = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[8];
        asBgra.CopyPixels(pixels, 8, 0);

        Assert.Equal(0, pixels[0]);        // first pixel black
        Assert.Equal(255, pixels[4]);      // second pixel white
    }

    [Fact]
    public void BlackAndWhite_OfAnOpaquePageIsStoredAsOneBitPerPixel()
    {
        BitmapSource result = PageQualityConverter.ApplyColorMode(
            Opaque(2, 100, 180), PageColorMode.BlackAndWhite, 160);

        Assert.Equal(PixelFormats.Indexed1, result.Format);
        Assert.Equal(1, result.Format.BitsPerPixel);
    }

    [Fact]
    public void AnImageWithTransparencyKeepsItsAlpha()
    {
        // The compact formats cannot carry alpha, so anything genuinely transparent must stay
        // 32-bit rather than silently lose it.
        BitmapSource source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
            null, new byte[] { 0, 0, 255, 123 }, 4);

        BitmapSource result = PageQualityConverter.ApplyColorMode(
            source, PageColorMode.Grayscale, 160);

        Assert.Equal(PixelFormats.Bgra32, result.Format);
        var pixels = new byte[4];
        result.CopyPixels(pixels, 4, 0);
        Assert.Equal(pixels[0], pixels[1]);
        Assert.Equal(pixels[1], pixels[2]);
        Assert.InRange(pixels[0], 75, 77);
        Assert.Equal(123, pixels[3]);
    }

    [Fact]
    public void ColourModeReturnsTheSourceUntouched()
    {
        BitmapSource source = Opaque(2, 10, 200);
        Assert.Same(source, PageQualityConverter.ApplyColorMode(source, PageColorMode.Color, 160));
    }

    [Fact]
    public void TheCompactFormsAreActuallySmaller()
    {
        // The point of the change, stated as a property rather than a claim.
        BitmapSource source = Opaque(8, 10, 200, 10, 200, 10, 200, 10, 200);

        var gray = PageQualityConverter.ApplyColorMode(source, PageColorMode.Grayscale, 160);
        var bilevel = PageQualityConverter.ApplyColorMode(source, PageColorMode.BlackAndWhite, 160);

        Assert.True(gray.Format.BitsPerPixel < source.Format.BitsPerPixel,
                    "grayscale should need fewer bits per pixel than BGRA");
        Assert.True(bilevel.Format.BitsPerPixel < gray.Format.BitsPerPixel,
                    "black and white should need fewer bits per pixel than grayscale");
    }
}
