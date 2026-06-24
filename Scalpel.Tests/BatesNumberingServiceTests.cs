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
    public class BatesNumberingServiceTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private static string MakeBlankPdf(int pages)
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_bates_in_{Guid.NewGuid():N}.pdf");
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
        public void Stamp_BatesNumber_AppearsSequentiallyOnEveryPage()
        {
            EnsureResolver();
            string input = MakeBlankPdf(3);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_bates_out_{Guid.NewGuid():N}.pdf");
            try
            {
                BatesNumberingService.StampFile(input, output, new StampOptions
                {
                    Template = "ACME-{n}",
                    StartNumber = 1,
                    DigitCount = 6,
                    Position = StampPosition.BottomRight,
                });

                Assert.Contains("ACME-000001", PageText(output, 1));
                Assert.Contains("ACME-000002", PageText(output, 2));
                Assert.Contains("ACME-000003", PageText(output, 3));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Stamp_PageNumbers_FormatsPageOfTotal()
        {
            EnsureResolver();
            string input = MakeBlankPdf(3);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_bates_out_{Guid.NewGuid():N}.pdf");
            try
            {
                BatesNumberingService.StampFile(input, output, new StampOptions
                {
                    Template = "Page {page} of {total}",
                    Position = StampPosition.BottomCenter,
                });

                Assert.Contains("Page 1 of 3", PageText(output, 1));
                Assert.Contains("Page 2 of 3", PageText(output, 2));
                Assert.Contains("Page 3 of 3", PageText(output, 3));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Stamp_RespectsPageRange_AndCountsOnlyStampedPages()
        {
            EnsureResolver();
            string input = MakeBlankPdf(3);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_bates_out_{Guid.NewGuid():N}.pdf");
            try
            {
                BatesNumberingService.StampFile(input, output, new StampOptions
                {
                    Template = "N{n}",
                    StartNumber = 100,
                    FromPage = 2,
                    ToPage = 3,
                });

                Assert.DoesNotContain("N100", PageText(output, 1));
                Assert.Contains("N100", PageText(output, 2));   // first stamped page = StartNumber
                Assert.Contains("N101", PageText(output, 3));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Stamp_HeaderText_TopCenter_IsBurnedIn()
        {
            EnsureResolver();
            string input = MakeBlankPdf(2);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_bates_out_{Guid.NewGuid():N}.pdf");
            try
            {
                BatesNumberingService.StampFile(input, output, new StampOptions
                {
                    Template = "CONFIDENTIAL",
                    Position = StampPosition.TopCenter,
                });

                Assert.Contains("CONFIDENTIAL", PageText(output, 1));
                Assert.Contains("CONFIDENTIAL", PageText(output, 2));
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void Stamp_PreservesPageCount()
        {
            EnsureResolver();
            string input = MakeBlankPdf(4);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_bates_out_{Guid.NewGuid():N}.pdf");
            try
            {
                BatesNumberingService.StampFile(input, output, new StampOptions { Template = "{n}" });
                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(4, doc.PageCount);
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
