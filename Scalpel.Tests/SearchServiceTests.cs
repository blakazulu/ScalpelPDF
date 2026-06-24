using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class SearchServiceTests
    {
        private readonly SearchService _svc = new();

        [Fact]
        public void Search_EmptyQuery_ReturnsEmpty()
        {
            var result = _svc.Search("irrelevant.pdf", "");
            Assert.Empty(result.ResultPages);
            Assert.Equal(0, result.TotalHits);
        }

        [Fact]
        public void Search_WhitespaceQuery_ReturnsEmpty()
        {
            var result = _svc.Search("irrelevant.pdf", "   ");
            Assert.Empty(result.ResultPages);
            Assert.Equal(0, result.TotalHits);
        }

        [Fact]
        public void Search_EmptyFilePath_ReturnsEmpty()
        {
            var result = _svc.Search("", "hello");
            Assert.Empty(result.ResultPages);
            Assert.Equal(0, result.TotalHits);
        }

        [Fact]
        public void Search_MissingFile_ReturnsEmpty()
        {
            // Should not throw; non-existent file produces no results.
            var result = _svc.Search(@"C:\does\not\exist.pdf", "hello");
            Assert.Empty(result.ResultPages);
            Assert.Equal(0, result.TotalHits);
        }

        [Fact]
        public void SearchResult_PageRects_EmptyByDefault()
        {
            var result = new SearchResult();
            Assert.Empty(result.PageRects);
            Assert.Empty(result.ResultPages);
            Assert.Equal(0, result.TotalHits);
        }
    }

    /// <summary>
    /// Hebrew search round-trip tests. In the [Collection("FontResolver")] group because they
    /// mutate <see cref="GlobalFontSettings.FontResolver"/>.
    /// </summary>
    [Collection("FontResolver")]
    public class SearchServiceHebrewTests
    {
        [Fact]
        public void Search_FindsHebrewWord_InLogicalOrderPdf()
        {
            // Build a PDF that stores a Hebrew word in logical order (no bidi reorder),
            // which is how real Hebrew PDFs store text and what PdfPig extracts.
            FontEmbeddingTestsEnsureResolver();
            // שלום = shin lamed vav mem-sofit = shalom (שלום)
            string word = "שלום";
            string path = Path.Combine(Path.GetTempPath(),
                $"scalpel_hesearch_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString(word, new XFont("Noto Sans Hebrew", 20),
                        XBrushes.Black, new XPoint(72, 72));
                    doc.Save(path);
                }
                // PdfSharpCore + Noto Sans Hebrew emits a /ToUnicode CMap, so PdfPig
                // can recover the Hebrew Unicode text. SearchService.Search finds it.
                var svc = new SearchService();
                var results = svc.Search(path, word);
                Assert.NotEmpty(results.ResultPages);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void FontEmbeddingTestsEnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
            // Ensure Noto Sans Hebrew is registered for this headless test.
            string noto = Path.Combine(RepoRootForSearch(), "Resources", "Fonts", "NotoSansHebrew-Regular.ttf");
            if (File.Exists(noto))
                PdfFontResolver.Instance.RegisterBundledFont(
                    "Noto Sans Hebrew", File.ReadAllBytes(noto), false, false);
        }

        private static string RepoRootForSearch()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
    }
}
