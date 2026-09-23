using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TrueTypeCollectionTests
    {
        private static string? SystemFont(string file)
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
            return File.Exists(p) ? p : null;
        }

        [Fact]
        public void ExtractFace_ProducesAStandaloneFontWithTheSameGlyphs()
        {
            string? ttc = SystemFont("cambria.ttc");
            if (ttc is null) return;   // not every Windows install carries Cambria
            byte[] data = File.ReadAllBytes(ttc);
            Assert.True(TrueTypeCollection.IsCollection(data));

            byte[]? face = TrueTypeCollection.ExtractFace(data, 0);

            Assert.NotNull(face);
            Assert.False(TrueTypeCollection.IsCollection(face));
            Assert.True(TrueTypeCmap.CoversCodepoint(face!, 'A'));
            Assert.Equal(TrueTypeName.Read(data, 0).Family, TrueTypeName.Read(face!, 0).Family);
        }

        [Fact]
        public void ExtractFace_RejectsBadInput()
        {
            Assert.Null(TrueTypeCollection.ExtractFace([1, 2, 3], 0));
            Assert.Null(TrueTypeCollection.ExtractFace(new byte[64], 0));
        }

        [Fact]
        public void AFontInstalledAsACollection_CanBeDrawnAndEmbedded()
        {
            // Regression: PdfSharpCore throws "TrueType collection fonts are not yet supported"
            // when handed a whole .ttc, which saved an edited Cambria line as a blank box.
            if (SystemFont("cambria.ttc") is null) return;
            PdfFontResolver.Install();

            var doc = new PdfDocument();
            using (var g = XGraphics.FromPdfPage(doc.AddPage()))
                g.DrawString("Heading", new XFont("Cambria", 14), XBrushes.Black, 40, 40);
            using var ms = new MemoryStream();
            doc.Save(ms);

            Assert.Contains("Cambria", System.Text.Encoding.ASCII.GetString(ms.ToArray()));
        }

        [Fact]
        public void BoldText_InAnUnknownFamily_UsesArialsRealBoldFace()
        {
            // PdfSharpCore ignores simulated bold, so the fallback must be a real bold face.
            if (SystemFont("arialbd.ttf") is null) return;
            PdfFontResolver.Install();
            var info = PdfFontResolver.Instance.ResolveTypeface("No Such Family 123", true, false);
            Assert.Equal("arial|1|0", info.FaceName);
        }
    }
}
