using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class RedactionServiceTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private sealed class FakeRasterizer : IPageRasterizer
        {
            private readonly int _pages;
            public FakeRasterizer(int pages) { _pages = pages; }
            public int PageCount => _pages;
            public (double widthPt, double heightPt) PageSizePt(int pageIndex) => (612, 792);
            public RasterPage RenderPage(int pageIndex)
                => new RasterPage(PdfRasterToolsTests.MakeJpeg(400, 520, 90), 400, 520);
        }

        // page 0 -> "TOPSECRET", page 1 -> "PUBLICINFO"
        private static string MakeTwoPageTextPdf()
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_redact_in_{Guid.NewGuid():N}.pdf");
            using var doc = new PdfDocument();
            string[] words = { "TOPSECRET", "PUBLICINFO" };
            foreach (var word in words)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(612);
                page.Height = XUnit.FromPoint(792);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawString(word, new XFont("Arial", 24), XBrushes.Black, new XPoint(72, 100));
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
        public void Redact_RemovesExtractableTextFromRedactedPage()
        {
            string input = MakeTwoPageTextPdf();
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_redact_out_{Guid.NewGuid():N}.pdf");
            try
            {
                Assert.Contains("TOPSECRET", PageText(input, 1)); // sanity: starts extractable

                RedactionService.Redact(input, new FakeRasterizer(2), new[]
                {
                    new RedactRect { PageIndex = 0, XPt = 72, YPt = 80, WidthPt = 200, HeightPt = 40 },
                }, output);

                Assert.DoesNotContain("TOPSECRET", PageText(output, 1)); // flattened to image — gone
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Redact_PreservesTextOnUntouchedPages()
        {
            string input = MakeTwoPageTextPdf();
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_redact_out_{Guid.NewGuid():N}.pdf");
            try
            {
                RedactionService.Redact(input, new FakeRasterizer(2), new[]
                {
                    new RedactRect { PageIndex = 0, XPt = 72, YPt = 80, WidthPt = 200, HeightPt = 40 },
                }, output);

                Assert.Contains("PUBLICINFO", PageText(output, 2)); // page 1 untouched, still vector text
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Redact_PreservesPageCount()
        {
            string input = MakeTwoPageTextPdf();
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_redact_out_{Guid.NewGuid():N}.pdf");
            try
            {
                RedactionService.Redact(input, new FakeRasterizer(2), new[]
                {
                    new RedactRect { PageIndex = 1, XPt = 72, YPt = 80, WidthPt = 200, HeightPt = 40 },
                }, output);

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(2, doc.PageCount);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Redact_NoRects_CopiesAllPagesWithTextIntact()
        {
            string input = MakeTwoPageTextPdf();
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_redact_out_{Guid.NewGuid():N}.pdf");
            try
            {
                RedactionService.Redact(input, new FakeRasterizer(2),
                    new List<RedactRect>(), output);

                Assert.Contains("TOPSECRET", PageText(output, 1));
                Assert.Contains("PUBLICINFO", PageText(output, 2));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Redact_MalformedInput_StillRedacts_ByFlatteningAllPages()
        {
            // Real-world reproduction of the customer's "Unexpected token 'xref' in PDF stream"
            // error: PdfSharpCore's parser rejects the file. PDFium (the rasterizer) is tolerant,
            // so redaction must still succeed by flattening every page rather than throwing.
            EnsureResolver();
            string input = Path.Combine(Path.GetTempPath(), $"scalpel_redact_bad_{Guid.NewGuid():N}.pdf");
            File.WriteAllText(input, "%PDF-1.4\nthis is not a parseable body\nxref\ntrailer\n%%EOF\n");
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_redact_out_{Guid.NewGuid():N}.pdf");
            try
            {
                RedactionService.Redact(input, new FakeRasterizer(3), new[]
                {
                    new RedactRect { PageIndex = 0, XPt = 10, YPt = 10, WidthPt = 50, HeightPt = 20 },
                }, output);

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(3, doc.PageCount); // page count + images came from the rasterizer
            }
            finally { Cleanup(input, output); }
        }

        private static void Cleanup(params string[] paths)
        {
            foreach (var p in paths)
                if (p != null && File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
