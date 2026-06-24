using System;
using System.IO;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class PdfCompressionServiceTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        /// <summary>Fake rasterizer returning deterministic high-frequency JPEGs.</summary>
        private sealed class FakeRasterizer : IPageRasterizer
        {
            private readonly int _pages;
            public FakeRasterizer(int pages) { _pages = pages; }
            public int PageCount => _pages;
            public (double widthPt, double heightPt) PageSizePt(int pageIndex) => (612, 792);
            public RasterPage RenderPage(int pageIndex)
                => new RasterPage(PdfRasterToolsTests.MakeJpeg(400, 520, 95), 400, 520);
        }

        private static string Tmp() =>
            Path.Combine(Path.GetTempPath(), $"scalpel_compress_{Guid.NewGuid():N}.pdf");

        [Fact]
        public void Compress_PreservesPageCount()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                PdfCompressionService.Compress(new FakeRasterizer(3),
                    new CompressionOptions { JpegQuality = 50 }, output);
                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(3, doc.PageCount);
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void Compress_PreservesPagePointSize()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                PdfCompressionService.Compress(new FakeRasterizer(1),
                    new CompressionOptions { JpegQuality = 50 }, output);
                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(612, doc.Pages[0].Width.Point, 1);
                Assert.Equal(792, doc.Pages[0].Height.Point, 1);
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void Compress_LowerQuality_ProducesSmallerFile()
        {
            EnsureResolver();
            string hiOut = Tmp();
            string loOut = Tmp();
            try
            {
                PdfCompressionService.Compress(new FakeRasterizer(2),
                    new CompressionOptions { JpegQuality = 90 }, hiOut);
                PdfCompressionService.Compress(new FakeRasterizer(2),
                    new CompressionOptions { JpegQuality = 20 }, loOut);

                long hi = new FileInfo(hiOut).Length;
                long lo = new FileInfo(loOut).Length;
                Assert.True(lo < hi, $"q20 ({lo}) should be smaller than q90 ({hi})");
            }
            finally { Cleanup(hiOut, loOut); }
        }

        [Fact]
        public void Compress_MaxDimension_ShrinksFurther()
        {
            EnsureResolver();
            string full = Tmp();
            string clamped = Tmp();
            try
            {
                PdfCompressionService.Compress(new FakeRasterizer(1),
                    new CompressionOptions { JpegQuality = 60, MaxImageDimension = 0 }, full);
                PdfCompressionService.Compress(new FakeRasterizer(1),
                    new CompressionOptions { JpegQuality = 60, MaxImageDimension = 150 }, clamped);

                Assert.True(new FileInfo(clamped).Length < new FileInfo(full).Length);
            }
            finally { Cleanup(full, clamped); }
        }

        private static void Cleanup(params string[] paths)
        {
            foreach (var p in paths)
                if (p != null && File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
