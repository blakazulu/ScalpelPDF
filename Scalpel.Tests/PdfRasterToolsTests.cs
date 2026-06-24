using System;
using System.IO;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class PdfRasterToolsTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        /// <summary>A deterministic high-frequency image so JPEG quality affects encoded size.</summary>
        internal static byte[] MakeJpeg(int w, int h, int quality)
        {
            using var img = new Image<Rgba32>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte r = (byte)((x * 37 + y * 17) & 0xFF);
                    byte g = (byte)((x * 5 + y * 71) & 0xFF);
                    byte b = (byte)((x ^ (y * 3)) & 0xFF);
                    img[x, y] = new Rgba32(r, g, b);
                }
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
            return ms.ToArray();
        }

        [Fact]
        public void ReencodeJpeg_LowerQuality_ProducesSmallerBytes()
        {
            byte[] hi = MakeJpeg(256, 256, 95);
            byte[] lo = PdfRasterTools.ReencodeJpeg(hi, 20);
            Assert.True(lo.Length < hi.Length, $"q20 ({lo.Length}) should be smaller than q95 ({hi.Length})");
            using var decoded = Image.Load(lo);
            Assert.Equal(256, decoded.Width);
            Assert.Equal(256, decoded.Height);
        }

        [Fact]
        public void ReencodeJpeg_MaxDimension_DownscalesProportionally()
        {
            byte[] src = MakeJpeg(400, 200, 90);
            byte[] outBytes = PdfRasterTools.ReencodeJpeg(src, 80, maxDimension: 100);
            using var decoded = Image.Load(outBytes);
            Assert.Equal(100, decoded.Width);
            Assert.Equal(50, decoded.Height);
        }

        [Fact]
        public void AppendImagePage_AddsPageAtRequestedPointSize()
        {
            EnsureResolver();
            byte[] jpg = MakeJpeg(120, 80, 80);
            using var doc = new PdfDocument();
            PdfRasterTools.AppendImagePage(doc, jpg, 300, 200);
            Assert.Equal(1, doc.PageCount);
            Assert.Equal(300, doc.Pages[0].Width.Point, 1);
            Assert.Equal(200, doc.Pages[0].Height.Point, 1);
        }
    }
}
