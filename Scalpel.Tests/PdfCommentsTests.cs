using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Reading the comments a reviewer left in a PDF. Scalpel paints the annotations now, but a
    /// sticky note shows only its icon, so the words have to be surfaced separately.
    /// </summary>
    public class PdfCommentsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "scalpel-comments-" + Guid.NewGuid().ToString("N")[..8]);

        public PdfCommentsTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
            GC.SuppressFinalize(this);
        }

        private static PdfDictionary Annot(PdfDocument doc, string subtype,
                                           string? contents, string? author,
                                           string? modified = null, bool reply = false)
        {
            var a = new PdfDictionary(doc);
            a.Elements["/Type"] = new PdfName("/Annot");
            a.Elements["/Subtype"] = new PdfName("/" + subtype);
            var rect = new PdfArray(doc);
            foreach (var v in new[] { 10, 10, 40, 40 }) rect.Elements.Add(new PdfReal(v));
            a.Elements["/Rect"] = rect;
            if (contents is not null) a.Elements["/Contents"] = new PdfString(contents);
            if (author is not null) a.Elements["/T"] = new PdfString(author);
            if (modified is not null) a.Elements["/M"] = new PdfString(modified);
            if (reply) a.Elements["/IRT"] = new PdfDictionary(doc);
            return a;
        }

        private string MakePdf(string name, Action<PdfDocument, PdfPage, PdfArray> addAnnots, int pages = 1)
        {
            string p = Path.Combine(_dir, name);
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);
                var annots = new PdfArray(doc);
                addAnnots(doc, page, annots);
                if (annots.Elements.Count > 0) page.Elements["/Annots"] = annots;
            }
            doc.Save(p);
            return p;
        }

        [Fact]
        public void ReadsAStickyNoteWithItsAuthorAndText()
        {
            string p = MakePdf("note.pdf", (d, _, annots) =>
                annots.Elements.Add(Annot(d, "Text", "Please check this figure", "Reviewer A", "D:20260830120000Z")));

            var comments = PdfComments.Read(p);
            var c = Assert.Single(comments);
            Assert.Equal(1, c.PageNumber);
            Assert.Equal("Text", c.Kind);
            Assert.Equal("Reviewer A", c.Author);
            Assert.Equal("Please check this figure", c.Contents);
            Assert.False(c.IsReply);
        }

        [Fact]
        public void ReadsANoteAttachedToAHighlight()
        {
            string p = MakePdf("hl.pdf", (d, _, annots) =>
                annots.Elements.Add(Annot(d, "Highlight", "Wrong figure here", "QA")));

            var c = Assert.Single(PdfComments.Read(p));
            Assert.Equal("Highlight", c.Kind);
            Assert.Equal("Wrong figure here", c.Contents);
        }

        [Fact]
        public void MarksRepliesAsReplies()
        {
            string p = MakePdf("reply.pdf", (d, _, annots) =>
            {
                annots.Elements.Add(Annot(d, "Text", "Original", "A"));
                annots.Elements.Add(Annot(d, "Text", "Agreed", "B", reply: true));
            });

            var comments = PdfComments.Read(p);
            Assert.Equal(2, comments.Count);
            Assert.Contains(comments, c => c.IsReply && c.Contents == "Agreed");
            Assert.Contains(comments, c => !c.IsReply && c.Contents == "Original");
        }

        [Fact]
        public void IgnoresDecorationWithNoTextOrAuthor()
        {
            // A plain highlight nobody wrote a note on is not a comment.
            string p = MakePdf("plain.pdf", (d, _, annots) =>
                annots.Elements.Add(Annot(d, "Highlight", null, null)));

            Assert.Empty(PdfComments.Read(p));
        }

        [Fact]
        public void IgnoresLinksAndFormWidgets()
        {
            string p = MakePdf("widgets.pdf", (d, _, annots) =>
            {
                annots.Elements.Add(Annot(d, "Link", "http://example.com", null));
                annots.Elements.Add(Annot(d, "Widget", "field value", "fieldname"));
            });

            Assert.Empty(PdfComments.Read(p));
        }

        [Fact]
        public void ReportsCommentsInPageOrder()
        {
            string p = Path.Combine(_dir, "multi.pdf");
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < 3; i++)
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(200);
                    page.Height = XUnit.FromPoint(200);
                    var annots = new PdfArray(doc);
                    annots.Elements.Add(Annot(doc, "Text", $"note on page {i + 1}", "A"));
                    page.Elements["/Annots"] = annots;
                }
                doc.Save(p);
            }

            var comments = PdfComments.Read(p);
            Assert.Equal(3, comments.Count);
            Assert.Equal([1, 2, 3], comments.Select(c => c.PageNumber).ToArray());
        }

        [Fact]
        public void ADocumentWithNoAnnotationsYieldsNothing()
        {
            string p = MakePdf("bare.pdf", (_, _, _) => { });
            Assert.Empty(PdfComments.Read(p));
        }

        [Fact]
        public void AnUnreadableFileYieldsNothingRatherThanThrowing()
        {
            string p = Path.Combine(_dir, "junk.pdf");
            File.WriteAllBytes(p, new byte[] { 9, 9, 9 });
            Assert.Empty(PdfComments.Read(p));
            Assert.Empty(PdfComments.Read(Path.Combine(_dir, "absent.pdf")));
        }

        [Fact]
        public void FormatGroupsByPageAndShowsAuthorAndText()
        {
            string p = MakePdf("fmt.pdf", (d, _, annots) =>
                annots.Elements.Add(Annot(d, "Text", "line one\nline two", "Reviewer A")));

            string text = PdfComments.Format(PdfComments.Read(p));
            Assert.Contains("Page 1", text);
            Assert.Contains("Reviewer A", text);
            Assert.Contains("line one", text);
            Assert.Contains("line two", text);
        }

        [Fact]
        public void FormatOfNothingIsEmpty()
            => Assert.Equal(string.Empty, PdfComments.Format([]));
    }
}
