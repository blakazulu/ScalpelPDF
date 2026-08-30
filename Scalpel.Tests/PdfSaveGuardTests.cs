using System;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class PdfSaveGuardTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "scalpel_saveguard_" + Guid.NewGuid().ToString("N"));

        public PdfSaveGuardTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private string NewSourcePdf()
        {
            string p = Path.Combine(_dir, "src.pdf");
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("hello", new XFont("Arial", 20), XBrushes.Black, new XPoint(50, 50));
            doc.Save(p);
            return p;
        }

        [Fact]
        public void Reading_empty_Outlines_then_saving_without_guard_produces_a_file_PdfSharp_cannot_reopen()
        {
            // Documents the PdfSharpCore defect the guard exists for. If this ever passes,
            // the library was fixed and PrepareForSave can be retired.
            string src = NewSourcePdf(), outp = Path.Combine(_dir, "touched.pdf");
            using (var d = PdfReader.Open(src, PdfDocumentOpenMode.Modify)) { _ = d.Outlines.Count; d.Save(outp); }
            Assert.Throws<PdfReaderException>(() => PdfReader.Open(outp, PdfDocumentOpenMode.Modify));
        }

        [Fact]
        public void PrepareForSave_drops_the_dangling_empty_Outlines_so_the_saved_file_reopens()
        {
            string src = NewSourcePdf(), outp = Path.Combine(_dir, "guarded.pdf");
            using (var d = PdfReader.Open(src, PdfDocumentOpenMode.Modify))
            {
                _ = d.Outlines.Count; // what LoadOutlines used to do on every open
                bool removed = PdfSaveGuard.PrepareForSave(d);
                Assert.True(removed);
                d.Save(outp);
            }
            using var reopened = PdfReader.Open(outp, PdfDocumentOpenMode.Modify);
            Assert.Equal(1, reopened.PageCount);
            Assert.False(reopened.Internals.Catalog.Elements.ContainsKey("/Outlines"));
        }

        [Fact]
        public void PrepareForSave_keeps_real_bookmarks()
        {
            string src = NewSourcePdf(), outp = Path.Combine(_dir, "bookmarked.pdf");
            using (var d = PdfReader.Open(src, PdfDocumentOpenMode.Modify))
            {
                d.Outlines.Add("Chapter 1", d.Pages[0]);
                bool removed = PdfSaveGuard.PrepareForSave(d);
                Assert.False(removed);
                d.Save(outp);
            }
            using var reopened = PdfReader.Open(outp, PdfDocumentOpenMode.Modify);
            Assert.Equal(1, reopened.Outlines.Count);
            Assert.Equal("Chapter 1", reopened.Outlines[0].Title);
        }

        [Fact]
        public void PrepareForSave_is_a_noop_when_Outlines_was_never_touched()
        {
            string src = NewSourcePdf();
            using var d = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            Assert.False(PdfSaveGuard.PrepareForSave(d));
            Assert.False(d.Internals.Catalog.Elements.ContainsKey("/Outlines"));
        }
    }
}
