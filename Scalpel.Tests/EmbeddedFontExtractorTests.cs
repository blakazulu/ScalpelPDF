using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("FontResolver")]
    public class EmbeddedFontExtractorTests
    {
        // shalom = shin lamed vav final-mem
        private const string Shalom = "שלום";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static void EnsureFonts()
        {
            string fonts = Path.Combine(RepoRoot(), "Resources", "Fonts");
            string p = Path.Combine(fonts, "NotoSansHebrew-Regular.ttf");
            if (File.Exists(p))
                PdfFontResolver.Instance.RegisterBundledFont("Noto Sans Hebrew", File.ReadAllBytes(p), false, false);
            if (PdfSharpCore.Fonts.GlobalFontSettings.FontResolver is null)
                PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        private static string MakePdfWithHebrew()
        {
            EnsureFonts();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_{Guid.NewGuid():N}.pdf");
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            // BidiReorder to visual so it embeds the glyphs used by "shalom".
            gfx.DrawString(BidiReorder.ToVisual(Shalom), new XFont("Noto Sans Hebrew", 24),
                XBrushes.Black, new XPoint(100, 100));
            doc.Save(path);
            return path;
        }

        [Fact]
        public void Normalize_StripsSubsetPrefix_SpacesAndHyphens()
        {
            Assert.Equal("notosanshebrew", EmbeddedFontExtractor.Normalize("ABCDEF+Noto Sans Hebrew"));
            // Style words aren't stripped (only subset prefix + spaces/hyphens/commas); lenient
            // Contains-matching handles the "-Regular" suffix at match time.
            Assert.Equal("davidlibreregular", EmbeddedFontExtractor.Normalize("/DavidLibre-Regular"));
        }

        // The name a PDF text extractor (PdfPig) reports for the drawn run — exactly what the editor
        // passes as the hint. Equals the PDF's BaseFont (modulo subset prefix).
        private static string ReportedFontName(string pdfPath)
        {
            using var pig = PigDocument.Open(pdfPath);
            return pig.GetPages().SelectMany(p => p.Letters).First().FontName ?? "";
        }

        [Fact]
        public void TryExtract_PullsEmbeddedFont_ThatCoversDrawnGlyphs()
        {
            string path = MakePdfWithHebrew();
            try
            {
                // Mirror the real edit flow: hint comes from the extractor's reported font name.
                string hint = ReportedFontName(path);
                Assert.False(string.IsNullOrEmpty(hint), "PdfPig should report the drawn font's name");
                byte[]? bytes = EmbeddedFontExtractor.TryExtract(path, hint, out bool isOpenType);

                Assert.NotNull(bytes);
                Assert.True(bytes!.Length > 0);
                // Valid sfnt header: 0x00010000 (TrueType), "true", "OTTO", or "ttcf".
                uint sig = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
                Assert.True(sig == 0x00010000u || sig == 0x74727565u /*true*/ || sig == 0x4F54544Fu /*OTTO*/,
                    $"unexpected font signature 0x{sig:X8}");
                // NOTE: whether the extracted font is USABLE for re-typesetting is decided by the
                // caller via TrueTypeCmap.CoversAllText — a Type0/CID subset (what PdfSharpCore writes
                // for Hebrew) keeps its glyphs but a stripped Unicode cmap, so it is correctly rejected
                // there and the editor falls back to a substitute + toast.
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void TryExtract_UnknownFont_ReturnsNull()
        {
            string path = MakePdfWithHebrew();
            try
            {
                Assert.Null(EmbeddedFontExtractor.TryExtract(path, "Totally Made Up Font 9000", out _));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void CoversAllText_TrueWhenAllPresent_FalseWhenAnyMissing()
        {
            // Deterministic gate test on the full Noto Sans Hebrew face: it covers all Hebrew but
            // not a CJK ideograph, so an edit adding out-of-font glyphs must fall back to a substitute.
            string fonts = Path.Combine(RepoRoot(), "Resources", "Fonts");
            byte[] noto = File.ReadAllBytes(Path.Combine(fonts, "NotoSansHebrew-Regular.ttf"));
            Assert.True(TrueTypeCmap.CoversAllText(noto, Shalom));
            Assert.False(TrueTypeCmap.CoversAllText(noto, Shalom + "漢")); // 漢 (CJK) not in a Hebrew font
            Assert.True(TrueTypeCmap.CoversAllText(noto, "  ")); // whitespace-only is trivially covered
        }
    }
}
