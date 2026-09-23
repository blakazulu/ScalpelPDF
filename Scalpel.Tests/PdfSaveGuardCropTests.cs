using System.IO;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Covers the save-time repairs beyond the dangling-outline case that PdfSaveGuardTests owns:
    /// degenerate crop boxes, invalidated signature values, and the page-size range helpers.
    /// </summary>
    public class PdfSaveGuardCropTests
    {
        private static PdfDocument NewDoc(int pages = 1)
        {
            var doc = new PdfDocument();
            for (int i = 0; i < pages; i++) doc.AddPage();
            return doc;
        }

        private static PdfArray Box(PdfDocument doc, double x1, double y1, double x2, double y2)
        {
            var arr = new PdfArray(doc);
            arr.Elements.Add(new PdfReal(x1));
            arr.Elements.Add(new PdfReal(y1));
            arr.Elements.Add(new PdfReal(x2));
            arr.Elements.Add(new PdfReal(y2));
            return arr;
        }

        [Fact]
        public void RemovesZeroSizeCropBox()
        {
            var doc = NewDoc();
            doc.Pages[0].Elements["/CropBox"] = Box(doc, 0, 0, 0, 0);

            Assert.True(PdfSaveGuard.StripDegenerateCropBoxes(doc));
            Assert.False(doc.Pages[0].Elements.ContainsKey("/CropBox"));
        }

        [Fact]
        public void RemovesCropBoxThatDoesNotTouchTheMediaBox()
        {
            var doc = NewDoc();
            doc.Pages[0].Elements["/MediaBox"] = Box(doc, 0, 0, 595, 842);
            doc.Pages[0].Elements["/CropBox"] = Box(doc, 5000, 5000, 5100, 5100);

            Assert.True(PdfSaveGuard.StripDegenerateCropBoxes(doc));
            Assert.False(doc.Pages[0].Elements.ContainsKey("/CropBox"));
        }

        [Fact]
        public void KeepsAValidInsetCropBox()
        {
            var doc = NewDoc();
            doc.Pages[0].Elements["/MediaBox"] = Box(doc, 0, 0, 595, 842);
            doc.Pages[0].Elements["/CropBox"] = Box(doc, 20, 20, 575, 822);

            Assert.False(PdfSaveGuard.StripDegenerateCropBoxes(doc));
            Assert.True(doc.Pages[0].Elements.ContainsKey("/CropBox"));
        }

        [Fact]
        public void LeavesPagesWithoutACropBoxAlone()
        {
            var doc = NewDoc(3);
            Assert.False(PdfSaveGuard.StripDegenerateCropBoxes(doc));
            for (int i = 0; i < 3; i++)
                Assert.False(doc.Pages[i].Elements.ContainsKey("/CropBox"));
        }

        [Fact]
        public void ClearsInvalidatedSignatureValueButKeepsTheField()
        {
            var doc = NewDoc();
            var sig = new PdfDictionary(doc);
            sig.Elements["/FT"] = new PdfName("/Sig");
            sig.Elements["/T"] = new PdfString("Signature1");
            sig.Elements["/V"] = new PdfDictionary(doc);

            var fields = new PdfArray(doc);
            fields.Elements.Add(sig);
            var acro = new PdfDictionary(doc);
            acro.Elements["/Fields"] = fields;
            doc.Internals.Catalog.Elements["/AcroForm"] = acro;
            doc.Internals.Catalog.Elements["/Perms"] = new PdfDictionary(doc);

            Assert.True(PdfSaveGuard.StripInvalidatedSignatures(doc));
            Assert.False(sig.Elements.ContainsKey("/V"));
            Assert.True(sig.Elements.ContainsKey("/T"));           // field kept for re-signing
            Assert.False(doc.Internals.Catalog.Elements.ContainsKey("/Perms"));
        }

        [Fact]
        public void SignatureScrubDoesNotThrowWithoutAnAcroForm()
        {
            var doc = NewDoc();
            var ex = Record.Exception(() => PdfSaveGuard.StripInvalidatedSignatures(doc));
            Assert.Null(ex);
        }

        [Fact]
        public void LeavesOrdinaryTextFieldsAlone()
        {
            var doc = NewDoc();
            var text = new PdfDictionary(doc);
            text.Elements["/FT"] = new PdfName("/Tx");
            text.Elements["/V"] = new PdfString("hello");

            var fields = new PdfArray(doc);
            fields.Elements.Add(text);
            var acro = new PdfDictionary(doc);
            acro.Elements["/Fields"] = fields;
            doc.Internals.Catalog.Elements["/AcroForm"] = acro;

            PdfSaveGuard.StripInvalidatedSignatures(doc);
            Assert.True(text.Elements.ContainsKey("/V"));
        }

        [Theory]
        [InlineData(2.0, false)]
        [InlineData(3.0, true)]
        [InlineData(595.0, true)]
        [InlineData(14400.0, true)]
        [InlineData(20000.0, false)]
        public void RangeCheckMatchesAdobeLimits(double points, bool expected)
            => Assert.Equal(expected, PdfSaveGuard.IsInRange(points));

        [Fact]
        public void FindsPagesOutsideTheSupportedRange()
        {
            var doc = NewDoc(2);
            doc.Pages[1].Width = PdfSharpCore.Drawing.XUnit.FromPoint(20000);

            var bad = PdfSaveGuard.FindPagesOutsideRange(doc);
            Assert.Equal([1], bad);
        }

        [Fact]
        public void ScaleFactorBringsAnOversizePageBackInsideTheRange()
        {
            double f = PdfSaveGuard.ScaleFactorToRange(20000, 10000);
            Assert.True(PdfSaveGuard.IsInRange(20000 * f));
            Assert.True(PdfSaveGuard.IsInRange(10000 * f));
            // aspect ratio preserved
            Assert.Equal(2.0, (20000 * f) / (10000 * f), 6);
        }

        [Fact]
        public void ScaleFactorGrowsAnUndersizePage()
        {
            double f = PdfSaveGuard.ScaleFactorToRange(1, 2);
            Assert.True(PdfSaveGuard.IsInRange(1 * f));
            Assert.True(PdfSaveGuard.IsInRange(2 * f));
        }

        [Fact]
        public void ScaleFactorIsIdentityForANormalPage()
            => Assert.Equal(1.0, PdfSaveGuard.ScaleFactorToRange(595, 842), 6);

        [Fact]
        public void SaveHelperWritesAReadableFileAndRepairsAsItGoes()
        {
            var doc = NewDoc();
            doc.Pages[0].Elements["/CropBox"] = Box(doc, 0, 0, 0, 0);

            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
            try
            {
                PdfSaveGuard.Save(doc, path);
                using var reopened = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
                Assert.Equal(1, reopened.PageCount);
                Assert.False(reopened.Pages[0].Elements.ContainsKey("/CropBox"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
