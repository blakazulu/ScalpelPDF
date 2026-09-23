using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scalpel.Services
{
    /// <summary>Color treatment for print preview / print output.</summary>
    public static class PrintColorConverter
    {
        /// <summary>Returns a frozen BGRA32 copy of <paramref name="source"/> with every pixel
        /// replaced by its perceptual luminance (alpha preserved).</summary>
        public static BitmapSource CreateGrayscaleBitmap(BitmapSource source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            BitmapSource bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = checked(bgra.PixelWidth * 4);
            byte[] pixels = new byte[checked(stride * bgra.PixelHeight)];
            bgra.CopyPixels(pixels, stride, 0);
            for (int index = 0; index < pixels.Length; index += 4)
            {
                byte gray = (byte)((pixels[index + 2] * 77
                    + pixels[index + 1] * 150 + pixels[index] * 29 + 128) >> 8);
                pixels[index] = gray;
                pixels[index + 1] = gray;
                pixels[index + 2] = gray;
            }

            BitmapSource result = BitmapSource.Create(
                bgra.PixelWidth, bgra.PixelHeight, bgra.DpiX, bgra.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
    }
}
