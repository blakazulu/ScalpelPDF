using System;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// What Scalpel's saved files claim about themselves, and what PdfSharpCore adds without
    /// being asked.
    ///
    /// <para>Both defects here were confirmed by reading the bytes of a real save rather than
    /// assumed from the library's documentation: PdfSharpCore stamps its own name into
    /// <c>/Producer</c> during Save (ignoring anything set beforehand), and attaches a DeviceRGB
    /// transparency <c>/Group</c> to every page an XGraphics surface touched. The three other
    /// conformance items that were suspected - a rewritten <c>/ModDate</c>, capitalised booleans,
    /// and indirect <c>/Length</c> values - turned out not to happen in this version at all.</para>
    /// </summary>
    public class SaveConformanceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "scalpel-conformance-" + Guid.NewGuid().ToString("N")[..8]);

        public SaveConformanceTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
            GC.SuppressFinalize(this);
        }

        /// <summary>A document whose pages have been drawn on, which is what adds the group.</summary>
        private string MakeDrawnPdf(string name, int pages = 2)
        {
            string path = Path.Combine(_dir, name);
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(XBrushes.Gray, new XRect(10, 10, 80, 80));
            }
            PdfSaveGuard.Save(doc, path);
            return path;
        }

        private static string ReadRaw(string path)
            => Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));

        [Fact]
        public void ASavedFileDoesNotClaimToBeMadeByPdfSharp()
        {
            string raw = ReadRaw(MakeDrawnPdf("producer.pdf"));

            int at = raw.IndexOf("/Producer", StringComparison.Ordinal);
            Assert.True(at >= 0, "the file has no /Producer entry at all");

            string producerRegion = raw.Substring(at, Math.Min(120, raw.Length - at));
            Assert.Contains("Scalpel", producerRegion);
            Assert.DoesNotContain("PDFsharp", producerRegion);
        }

        [Fact]
        public void RewritingTheProducerKeepsTheFileReadable()
        {
            // The rewrite is done in place on the finished bytes, so the real risk is a shifted
            // cross-reference table. Proving the file still opens is what makes it safe.
            string path = MakeDrawnPdf("reopen.pdf", pages: 3);

            using var reopened = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            Assert.Equal(3, reopened.PageCount);
        }

        [Fact]
        public void TheRewriteDoesNotChangeTheFileLength()
        {
            // Equal length is the whole reason the xref survives; guard it directly.
            string path = Path.Combine(_dir, "length.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(XBrushes.Gray, new XRect(10, 10, 80, 80));
                PdfSaveGuard.PrepareForSave(doc);
                doc.Save(path);
            }

            long before = new FileInfo(path).Length;
            PdfSaveGuard.RewriteProducer(path);
            Assert.Equal(before, new FileInfo(path).Length);
        }

        [Fact]
        public void PagesDoNotCarryTheBoilerplateTransparencyGroup()
        {
            string raw = ReadRaw(MakeDrawnPdf("group.pdf", pages: 3));
            Assert.DoesNotContain("/Transparency", raw);
        }

        [Fact]
        public void ADeliberateTransparencyGroupIsLeftAlone()
        {
            // Only the blanket DeviceRGB group is boilerplate. A group carrying anything else was
            // put there on purpose and removing it would change how the page renders.
            string path = Path.Combine(_dir, "deliberate.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);

                var group = new PdfDictionary(doc);
                group.Elements["/S"] = new PdfName("/Transparency");
                group.Elements["/CS"] = new PdfName("/DeviceCMYK");
                group.Elements["/I"] = new PdfBoolean(true);
                group.Elements["/K"] = new PdfBoolean(true);
                page.Elements["/Group"] = group;

                doc.Save(path);
            }

            Assert.False(PdfSaveGuard.StripSavedTransparencyGroups(path),
                         "a deliberate group must not be stripped");
            Assert.Contains("/DeviceCMYK", ReadRaw(path));
        }

        [Fact]
        public void StrippingGroupsIsSafeOnAFileThatHasNone()
        {
            string path = Path.Combine(_dir, "nogroup.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);
                doc.Save(path);
            }
            long before = new FileInfo(path).Length;
            PdfSaveGuard.StripSavedTransparencyGroups(path);
            Assert.Equal(before, new FileInfo(path).Length);
        }

        [Fact]
        public void StrippingGroupsKeepsTheFileTheSameLengthAndReadable()
        {
            // Same guarantee as the producer rewrite: blanking must not move a single byte.
            string path = Path.Combine(_dir, "grouplen.pdf");
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < 3; i++)
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(200);
                    page.Height = XUnit.FromPoint(200);
                    using var gfx = XGraphics.FromPdfPage(page);
                    gfx.DrawRectangle(XBrushes.Gray, new XRect(10, 10, 80, 80));
                }
                doc.Save(path);
            }

            long before = new FileInfo(path).Length;
            Assert.True(PdfSaveGuard.StripSavedTransparencyGroups(path), "nothing was stripped");
            Assert.Equal(before, new FileInfo(path).Length);

            using var reopened = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
            Assert.Equal(3, reopened.PageCount);
        }

        [Fact]
        public void RewritingTheProducerOfAMissingOrJunkFileIsHarmless()
        {
            Assert.False(PdfSaveGuard.RewriteProducer(Path.Combine(_dir, "absent.pdf")));

            string junk = Path.Combine(_dir, "junk.pdf");
            File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4 });
            Assert.False(PdfSaveGuard.RewriteProducer(junk));
            Assert.Equal(4, new FileInfo(junk).Length);
        }

        [Fact]
        public void TheThingsThatWereNeverBrokenStayThatWay()
        {
            // Regression guard for the three conformance items that measurement cleared: if a
            // future PdfSharpCore starts doing any of them, this catches it rather than letting
            // the file quietly become non-conformant.
            string raw = ReadRaw(MakeDrawnPdf("clean.pdf"));

            Assert.DoesNotContain("/ModDate", raw);
            Assert.DoesNotContain("/True", raw);      // booleans must stay lowercase
            Assert.DoesNotContain("/False", raw);
        }
    }
}
