using System;
using System.Collections.Generic;
using System.IO;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class OcrServiceTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        // Regression: TSV must be requested via the -c parameter, NOT the `tsv` config file.
        // When --tessdata-dir is overridden to Scalpel's download folder (language data only, no
        // configs/), the `tsv` config is absent and tesseract silently emits plain text instead of
        // TSV — producing an OCR'd PDF with no searchable text layer.
        [Fact]
        public void BuildArguments_RequestsTsvViaParameter_NotConfigFile()
        {
            string args = TesseractCliOcrEngine.BuildArguments(@"C:\tmp\page.png", @"C:\data\tessdata", "eng");

            Assert.Contains("-c tessedit_create_tsv=1", args);
            Assert.Contains("--tessdata-dir \"C:\\data\\tessdata\"", args);
            Assert.Contains("-l eng", args);
            // Must NOT pass a bare `tsv` config token (the bug) — that needs configs/tsv on disk.
            Assert.DoesNotContain(" tsv", args);
        }

        [Fact]
        public void BuildArguments_OmitsTessdataDir_WhenBlank()
        {
            string args = TesseractCliOcrEngine.BuildArguments(@"C:\tmp\page.png", "", "eng");
            Assert.DoesNotContain("--tessdata-dir", args);
            Assert.Contains("-c tessedit_create_tsv=1", args);
        }

        private sealed class FakeRasterizer : IPageRasterizer
        {
            private readonly int _pages;
            public FakeRasterizer(int pages) { _pages = pages; }
            public int PageCount => _pages;
            public (double widthPt, double heightPt) PageSizePt(int pageIndex) => (612, 792);
            public RasterPage RenderPage(int pageIndex)
                => new RasterPage(PdfRasterToolsTests.MakeJpeg(400, 520, 85), 400, 520);
        }

        private sealed class FakeOcrEngine : IOcrEngine
        {
            private readonly string _word;
            public FakeOcrEngine(string word) { _word = word; }
            public OcrPageResult Recognize(byte[] imageBytes, double pageWidthPt, double pageHeightPt)
                => new OcrPageResult
                {
                    Words = { new OcrWord { Text = _word, XPt = 72, YPt = 100, WidthPt = 120, HeightPt = 18 } }
                };
        }

        private static string PageText(string path, int pageNumber1Based)
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            return doc.GetPage(pageNumber1Based).Text;
        }

        private static string Tmp() =>
            Path.Combine(Path.GetTempPath(), $"scalpel_ocr_{Guid.NewGuid():N}.pdf");

        [Fact]
        public void Write_EmbedsOcrTextAsSearchable()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                var pages = new List<OcrPage>
                {
                    new OcrPage
                    {
                        ImageBytes = PdfRasterToolsTests.MakeJpeg(300, 200, 85),
                        WidthPt = 612, HeightPt = 792,
                        Ocr = new OcrPageResult
                        {
                            Words =
                            {
                                new OcrWord { Text = "HELLO", XPt = 72, YPt = 100, WidthPt = 80, HeightPt = 20 },
                                new OcrWord { Text = "WORLD", XPt = 160, YPt = 100, WidthPt = 80, HeightPt = 20 },
                            }
                        }
                    }
                };
                SearchableLayerWriter.Write(pages, output);

                string text = PageText(output, 1);
                Assert.Contains("HELLO", text);
                Assert.Contains("WORLD", text);
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void Write_EmptyOcr_StillProducesImagePage()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                var pages = new List<OcrPage>
                {
                    new OcrPage
                    {
                        ImageBytes = PdfRasterToolsTests.MakeJpeg(200, 200, 85),
                        WidthPt = 400, HeightPt = 400,
                        Ocr = new OcrPageResult(),
                    }
                };
                SearchableLayerWriter.Write(pages, output);

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, doc.PageCount);
                Assert.Equal(400, doc.Pages[0].Width.Point, 1);
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void MakeSearchable_RunsEnginePerPage_AndWritesAllPages()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                OcrService.MakeSearchable(new FakeRasterizer(2), new FakeOcrEngine("SCANNED"), output);

                using (var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly))
                    Assert.Equal(2, doc.PageCount);

                Assert.Contains("SCANNED", PageText(output, 1));
                Assert.Contains("SCANNED", PageText(output, 2));
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void MakeSearchable_ReportsProgressPerPage()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                var calls = new System.Collections.Generic.List<(int cur, int total)>();
                OcrService.MakeSearchable(new FakeRasterizer(3), new FakeOcrEngine("X"), output,
                    onProgress: (cur, total) => calls.Add((cur, total)));
                // One call per page (1..3) plus a final (3,3) before writing.
                Assert.Equal(new[] { (1, 3), (2, 3), (3, 3), (3, 3) }, calls);
            }
            finally { Cleanup(output); }
        }

        [Fact]
        public void MakeSearchable_CancellationThrows_AndWritesNothing()
        {
            EnsureResolver();
            string output = Tmp();
            try
            {
                using var cts = new System.Threading.CancellationTokenSource();
                cts.Cancel();
                Assert.Throws<System.OperationCanceledException>(() =>
                    OcrService.MakeSearchable(new FakeRasterizer(2), new FakeOcrEngine("X"), output, cancel: cts.Token));
                Assert.False(File.Exists(output)); // aborted before any file was written
            }
            finally { Cleanup(output); }
        }

        private static void Cleanup(params string[] paths)
        {
            foreach (var p in paths)
                if (p != null && File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
