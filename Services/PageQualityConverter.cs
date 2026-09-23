using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scalpel.Services
{
    /// <summary>Output color treatment for a rendered page.</summary>
    public enum PageColorMode { Color, Grayscale, BlackAndWhite }

    /// <summary>
    /// Recolors a rendered page bitmap: perceptual-luminance grayscale, or a hard
    /// black-and-white threshold. Alpha is preserved; <see cref="PageColorMode.Color"/>
    /// returns the source untouched.
    /// </summary>
    public static class PageQualityConverter
    {
        /// <summary>Applies <paramref name="mode"/> to <paramref name="source"/>.
        /// <paramref name="threshold"/> (0..255) only matters for black-and-white: gray
        /// values at or above it become white, everything darker becomes black.
        ///
        /// <para>The result is stored in the narrowest format that carries the information:
        /// <see cref="PixelFormats.Gray8"/> for grayscale and <see cref="PixelFormats.Indexed1"/>
        /// for black and white. Writing all three modes as 32-bit BGRA - which is what this used
        /// to do - stores three identical channels for a gray page and spends 32 bits per pixel
        /// carrying one bit for a bilevel one, making an exported page roughly four to thirty
        /// times larger than it needs to be.</para></summary>
        public static BitmapSource ApplyColorMode(
            BitmapSource source, PageColorMode mode, int threshold)
        {
            if (mode == PageColorMode.Color) return source;

            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            int sourceStride = width * 4;
            var pixels = new byte[sourceStride * height];
            converted.CopyPixels(pixels, sourceStride, 0);
            int clampedThreshold = Math.Max(0, Math.Min(255, threshold));

            // Gray8 and Indexed1 have no alpha channel, so the compact forms are only used when
            // the page is fully opaque - which a rendered PDF page always is. An image that
            // actually carries transparency keeps the wider format and its alpha intact.
            BitmapSource result = IsFullyOpaque(pixels)
                ? (mode == PageColorMode.BlackAndWhite
                    ? ToBilevel(pixels, width, height, sourceStride, clampedThreshold,
                                converted.DpiX, converted.DpiY)
                    : ToGray(pixels, width, height, sourceStride, converted.DpiX, converted.DpiY))
                : ToBgraPreservingAlpha(pixels, width, height, sourceStride, mode,
                                        clampedThreshold, converted.DpiX, converted.DpiY);

            result.Freeze();
            return result;
        }

        /// <summary>True when every pixel is fully opaque.</summary>
        private static bool IsFullyOpaque(byte[] bgra)
        {
            for (int offset = 3; offset < bgra.Length; offset += 4)
                if (bgra[offset] != 255) return false;
            return true;
        }

        /// <summary>
        /// The original behaviour, kept for images that carry transparency: recolour in place and
        /// stay 32-bit so the alpha channel survives.
        /// </summary>
        private static BitmapSource ToBgraPreservingAlpha(byte[] bgra, int width, int height,
                                                          int stride, PageColorMode mode,
                                                          int threshold, double dpiX, double dpiY)
        {
            for (int offset = 0; offset + 3 < bgra.Length; offset += 4)
            {
                int gray = Luminance(bgra, offset);
                byte value = mode == PageColorMode.BlackAndWhite
                    ? (byte)(gray >= threshold ? 255 : 0)
                    : (byte)gray;
                bgra[offset] = bgra[offset + 1] = bgra[offset + 2] = value;
            }
            return BitmapSource.Create(width, height, dpiX, dpiY,
                                       PixelFormats.Bgra32, null, bgra, stride);
        }

        /// <summary>Perceptual luminance of the BGRA pixel at <paramref name="offset"/>.</summary>
        private static int Luminance(byte[] bgra, int offset)
            => (bgra[offset + 2] * 77 + bgra[offset + 1] * 150 + bgra[offset] * 29 + 128) >> 8;

        /// <summary>One byte per pixel: the same picture as gray BGRA, a quarter of the size.</summary>
        private static BitmapSource ToGray(byte[] bgra, int width, int height, int sourceStride,
                                           double dpiX, double dpiY)
        {
            int stride = width;
            var gray = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int from = y * sourceStride, to = y * stride;
                for (int x = 0; x < width; x++)
                    gray[to + x] = (byte)Luminance(bgra, from + x * 4);
            }
            return BitmapSource.Create(width, height, dpiX, dpiY,
                                       PixelFormats.Gray8, null, gray, stride);
        }

        /// <summary>
        /// One BIT per pixel against a black/white palette. Scanned text compresses dramatically
        /// in this form, which is the whole point of choosing black and white.
        /// </summary>
        private static BitmapSource ToBilevel(byte[] bgra, int width, int height, int sourceStride,
                                              int threshold, double dpiX, double dpiY)
        {
            int stride = (width + 7) / 8;                 // packed bits, rounded up to a byte
            var bits = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int from = y * sourceStride, to = y * stride;
                for (int x = 0; x < width; x++)
                {
                    // Palette index 1 is white; leaving the bit clear gives black.
                    if (Luminance(bgra, from + x * 4) >= threshold)
                        bits[to + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            var palette = new BitmapPalette(new List<Color> { Colors.Black, Colors.White });
            return BitmapSource.Create(width, height, dpiX, dpiY,
                                       PixelFormats.Indexed1, palette, bits, stride);
        }
    }
}
