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
    public class TransformServiceTests
    {
        private static void EnsureResolver()
        {
            PdfFontResolver.Install();
        }

        private static string MakeBlankPdf(int pages)
        {
            EnsureResolver();
            string path = Path.Combine(Path.GetTempPath(), $"scalpel_tf_in_{Guid.NewGuid():N}.pdf");
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

        // ---- pure size helper -----------------------------------------------------------------

        [Fact]
        public void TransformedSize_NoTurn_KeepsDimensions()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions());
            Assert.Equal(612, w, 3);
            Assert.Equal(792, h, 3);
        }

        [Fact]
        public void TransformedSize_90_SwapsWidthHeight()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions { QuarterTurns = 1 });
            Assert.Equal(792, w, 3);
            Assert.Equal(612, h, 3);
        }

        [Fact]
        public void TransformedSize_180_KeepsDimensions()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions { QuarterTurns = 2 });
            Assert.Equal(612, w, 3);
            Assert.Equal(792, h, 3);
        }

        [Fact]
        public void TransformedSize_270_SwapsWidthHeight()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions { QuarterTurns = 3 });
            Assert.Equal(792, w, 3);
            Assert.Equal(612, h, 3);
        }

        [Fact]
        public void TransformedSize_Scale_MultipliesBothAxes()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions { Scale = 2.0 });
            Assert.Equal(1224, w, 3);
            Assert.Equal(1584, h, 3);
        }

        [Fact]
        public void TransformedSize_TurnPlusScale_SwapsThenScales()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792,
                new TransformOptions { QuarterTurns = 1, Scale = 0.5 });
            Assert.Equal(396, w, 3); // 792 * 0.5
            Assert.Equal(306, h, 3); // 612 * 0.5
        }

        [Fact]
        public void TransformedSize_NegativeOrZeroScale_TreatedAsUnity()
        {
            var (w, h) = TransformService.TransformedSizePt(612, 792, new TransformOptions { Scale = 0 });
            Assert.Equal(612, w, 3);
            Assert.Equal(792, h, 3);
        }

        // ---- lossless quarter-turn round-trip (no rasterizer needed) ---------------------------

        [Fact]
        public void ApplyFile_QuarterTurn_PreservesPageCount_AndOpens()
        {
            string input = MakeBlankPdf(2);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_tf_out_{Guid.NewGuid():N}.pdf");
            try
            {
                TransformService.ApplyFile(input, output, new TransformOptions { QuarterTurns = 1 });

                Assert.True(File.Exists(output));
                Assert.True(new FileInfo(output).Length > 0);

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(2, doc.PageCount);
                // /Rotate bumped by 90° on every (in-range) page.
                Assert.Equal(90, ((doc.Pages[0].Rotate % 360) + 360) % 360);
                Assert.Equal(90, ((doc.Pages[1].Rotate % 360) + 360) % 360);
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void ApplyFile_QuarterTurn_RespectsPageRange()
        {
            string input = MakeBlankPdf(3);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_tf_out_{Guid.NewGuid():N}.pdf");
            try
            {
                TransformService.ApplyFile(input, output,
                    new TransformOptions { QuarterTurns = 1, FromPage = 2, ToPage = 2 });

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(0, ((doc.Pages[0].Rotate % 360) + 360) % 360); // untouched
                Assert.Equal(90, ((doc.Pages[1].Rotate % 360) + 360) % 360); // rotated
                Assert.Equal(0, ((doc.Pages[2].Rotate % 360) + 360) % 360); // untouched
            }
            finally { Cleanup(input, output); }
        }

        [Fact]
        public void ApplyFile_NoOpTransform_WritesNonEmptyOpenableFile()
        {
            string input = MakeBlankPdf(1);
            string output = Path.Combine(Path.GetTempPath(), $"scalpel_tf_out_{Guid.NewGuid():N}.pdf");
            try
            {
                var ex = Record.Exception(() =>
                    TransformService.ApplyFile(input, output, new TransformOptions()));
                Assert.Null(ex);
                Assert.True(File.Exists(output));
                Assert.True(new FileInfo(output).Length > 0);

                using var doc = PdfReader.Open(output, PdfDocumentOpenMode.ReadOnly);
                Assert.Equal(1, doc.PageCount);
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
