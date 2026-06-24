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
    public class FontEmbeddingTests
    {
        private static void EnsureResolver()
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = PdfFontResolver.Instance;
        }

        /// <summary>True if any font in the saved PDF has an embedded font program.</summary>
        public static bool HasEmbeddedFontProgram(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.ReadOnly);
            foreach (var obj in doc.Internals.GetAllObjects())
            {
                if (obj is PdfSharpCore.Pdf.PdfDictionary dict &&
                    (dict.Elements.ContainsKey("/FontFile2") ||
                     dict.Elements.ContainsKey("/FontFile3") ||
                     dict.Elements.ContainsKey("/FontFile")))
                    return true;
            }
            return false;
        }

        [Fact]
        public void DrawingSystemFont_EmbedsIt()
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Embedding check 12345", new XFont("Arial", 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(File.Exists(path));
                Assert.True(HasEmbeddedFontProgram(path), "saved PDF should embed the Arial font program");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void DrawingRegisteredBundledFont_EmbedsIt()
        {
            EnsureResolver();
            // Register the repo's bundled Geist as a uniquely-named family so this test
            // is independent of whether Geist is installed on the machine.
            string repo = RepoRoot();
            string geist = Path.Combine(repo, "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(geist), $"expected bundled font at {geist}");
            const string fam = "ScalpelBundledTestFont";
            PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(geist), false, false);

            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_b_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Bundled embed 12345", new XFont(fam, 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path), "saved PDF should embed the bundled font");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void DrawingSimulatedBold_StillEmbeds()
        {
            EnsureResolver();
            string geist = Path.Combine(RepoRoot(), "Resources", "Fonts", "Geist-Regular.ttf");
            Assert.True(File.Exists(geist), $"expected bundled font at {geist}");
            // Register a regular-only family under a unique name — no bold face registered.
            // ResolveTypeface("ScalpelSimBoldTestFont", bold=true) will return the regular face
            // with MustSimulateBold set; the saved PDF must still embed the font program.
            const string fam = "ScalpelSimBoldTestFont";
            PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(geist), false, false);
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_sim_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawString("Sim bold 12345", new XFont(fam, 14, XFontStyle.Bold), XBrushes.Black, new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path), "simulated-bold draw should still embed the regular face");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void NotoHebrew_FromRepo_CoversAlef_AndEmbeds()
        {
            EnsureResolver();
            string noto = Path.Combine(RepoRoot(), "Resources", "Fonts", "NotoSansHebrew-Regular.ttf");
            Assert.True(File.Exists(noto), $"expected bundled Hebrew font at {noto}");
            byte[] bytes = File.ReadAllBytes(noto);
            Assert.True(Scalpel.Services.TrueTypeCmap.CoversCodepoint(bytes, 0x05D0),
                "Noto Sans Hebrew must cover ALEF");

            const string fam = "ScalpelHebrewTestFont";
            PdfFontResolver.Instance.RegisterBundledFont(fam, bytes, false, false);
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_embed_he_{Guid.NewGuid():N}.pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    using var gfx = XGraphics.FromPdfPage(page);
                    // visual order is irrelevant to embedding; draw Hebrew glyphs
                    gfx.DrawString("םולש", new XFont(fam, 14), XBrushes.Black,
                        new XPoint(50, 50));
                    doc.Save(path);
                }
                Assert.True(HasEmbeddedFontProgram(path), "Hebrew font should embed");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
    }
}
