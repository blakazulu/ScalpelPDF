using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")] // global GlobalFontSettings state — no parallel runs
    public class WatermarkServiceTests
    {
        private static void EnsureResolver()
        {
            PdfFontResolver.Install();
        }

        private static string MakeBlankPdf(int pages)
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_wm_in_{Guid.NewGuid():N}.pdf");
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(612);  // US Letter
                page.Height = XUnit.FromPoint(792);
            }
            doc.Save(path);
            return path;
        }

        private static string PageText(string path, int pageNumber1Based)
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            return doc.GetPage(pageNumber1Based).Text;
        }

        [Fact]
        public void Defaults_AreSane()
        {
            var o = new WatermarkOptions();
            Assert.Null(o.Text);
            Assert.Equal("Geist", o.FontFamily);
            Assert.Equal(48, o.FontSize);
            Assert.Equal((byte)128, o.Color.R);
            Assert.Equal((byte)128, o.Color.G);
            Assert.Equal((byte)128, o.Color.B);
            Assert.Equal(0.30, o.Opacity, 3);
            Assert.Equal(45, o.RotationDegrees);
            Assert.Equal(WatermarkPosition.Center, o.Position);
            Assert.Null(o.ImagePath);
            Assert.Equal(1.0, o.ImageScale, 3);
            Assert.Null(o.FromPage);
            Assert.Null(o.ToPage);
        }

        [Fact]
        public void Apply_TextWatermark_IsBurnedIn_AndPreservesPageCount()
        {
            string input = MakeBlankPdf(2);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_wm_out_{Guid.NewGuid():N}.pdf");
            try
            {
                WatermarkService.ApplyFile(input, output, new WatermarkOptions
                {
                    Text = "CONFIDENTIAL",
                    RotationDegrees = 0, // axis-aligned so PdfPig extraction is unambiguous
                });

                Assert.Contains("CONFIDENTIAL", PageText(output, 1));
                Assert.Contains("CONFIDENTIAL", PageText(output, 2));

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(2, doc.PageCount);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Apply_RespectsPageRange_OnlyInRangePagesChange()
        {
            string input = MakeBlankPdf(3);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_wm_out_{Guid.NewGuid():N}.pdf");
            try
            {
                WatermarkService.ApplyFile(input, output, new WatermarkOptions
                {
                    Text = "SAMPLE",
                    RotationDegrees = 0,
                    FromPage = 2,
                    ToPage = 2,
                });

                Assert.DoesNotContain("SAMPLE", PageText(output, 1)); // out of range
                Assert.Contains("SAMPLE", PageText(output, 2));       // in range
                Assert.DoesNotContain("SAMPLE", PageText(output, 3)); // out of range
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Apply_Tiled_DoesNotThrow_AndPreservesPageCount()
        {
            string input = MakeBlankPdf(1);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_wm_out_{Guid.NewGuid():N}.pdf");
            try
            {
                var ex = Record.Exception(() => WatermarkService.ApplyFile(input, output, new WatermarkOptions
                {
                    Text = "DRAFT",
                    Position = WatermarkPosition.Tiled,
                    FontSize = 36,
                }));

                Assert.Null(ex);
                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, doc.PageCount);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Apply_MissingImagePath_IsIgnored_NoThrow()
        {
            EnsureResolver();
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);

            var ex = Record.Exception(() => WatermarkService.Apply(doc, new WatermarkOptions
            {
                // no Text, and an image path that does not exist → a clean no-op, never a throw
                ImagePath = Path.Combine(Path.GetTempPath(), $"scalpel_no_such_image_{Guid.NewGuid():N}.png"),
            }));

            Assert.Null(ex);
            Assert.Equal(1, doc.PageCount);
        }

        private static void Cleanup(params string[] paths)
        {
            foreach (var p in paths)
                if (p != null && File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
