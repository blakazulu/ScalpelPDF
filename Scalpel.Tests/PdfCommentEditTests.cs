using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Editing the comments a PDF carries: correcting the text, changing who it is attributed to,
    /// and removing one outright.
    /// </summary>
    public class PdfCommentEditTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "scalpel-cmt-edit-" + Guid.NewGuid().ToString("N")[..8]);

        public PdfCommentEditTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
            GC.SuppressFinalize(this);
        }

        private static PdfDictionary Annot(PdfDocument doc, string subtype, string? contents,
                                           string? author, bool withAppearance = false)
        {
            var a = new PdfDictionary(doc);
            a.Elements["/Type"] = new PdfName("/Annot");
            a.Elements["/Subtype"] = new PdfName("/" + subtype);
            var rect = new PdfArray(doc);
            foreach (var v in new[] { 10, 10, 40, 40 }) rect.Elements.Add(new PdfReal(v));
            a.Elements["/Rect"] = rect;
            if (contents is not null) a.Elements["/Contents"] = new PdfString(contents);
            if (author is not null) a.Elements["/T"] = new PdfString(author);
            if (withAppearance) a.Elements["/AP"] = new PdfDictionary(doc);
            return a;
        }

        /// <summary>A one-page document carrying the given comments, kept open for editing.</summary>
        private static PdfDocument MakeDoc(params (string kind, string text, string author)[] notes)
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(200);
            page.Height = XUnit.FromPoint(200);
            var annots = new PdfArray(doc);
            foreach (var (kind, text, author) in notes)
                annots.Elements.Add(Annot(doc, kind, text, author, withAppearance: true));
            page.Elements["/Annots"] = annots;
            return doc;
        }

        [Fact]
        public void EditingAcommentChangesItsText()
        {
            using var doc = MakeDoc(("Text", "Chekc this figure", "Reviewer A"));
            var before = PdfComments.Read(doc).Single();

            Assert.True(PdfComments.UpdateContents(doc, before, "Check this figure"));

            var after = PdfComments.Read(doc).Single();
            Assert.Equal("Check this figure", after.Contents);
            Assert.Equal("Reviewer A", after.Author);
        }

        [Fact]
        public void EditingDropsTheCachedAppearanceSoOtherReadersShowTheNewText()
        {
            // The reason this matters: a viewer that finds a stale /AP draws the OLD words, so the
            // edit would appear to work in Scalpel and nowhere else.
            using var doc = MakeDoc(("Text", "original", "A"));
            var annots = doc.Pages[0].Elements.GetArray("/Annots")!;
            var dict = (PdfDictionary)annots.Elements[0];
            Assert.True(dict.Elements.ContainsKey("/AP"), "fixture should start with an appearance");

            PdfComments.UpdateContents(doc, PdfComments.Read(doc).Single(), "corrected");

            Assert.False(dict.Elements.ContainsKey("/AP"));
        }

        [Fact]
        public void EditingStampsTheModifiedDate()
        {
            using var doc = MakeDoc(("Text", "note", "A"));
            Assert.Empty(PdfComments.Read(doc).Single().Modified);

            PdfComments.UpdateContents(doc, PdfComments.Read(doc).Single(), "edited");

            Assert.StartsWith("D:", PdfComments.Read(doc).Single().Modified);
        }

        [Fact]
        public void TheAuthorCanBeChanged()
        {
            using var doc = MakeDoc(("Text", "note", "Old Name"));
            Assert.True(PdfComments.UpdateAuthor(doc, PdfComments.Read(doc).Single(), "New Name"));

            var after = PdfComments.Read(doc).Single();
            Assert.Equal("New Name", after.Author);
            Assert.Equal("note", after.Contents);
        }

        [Fact]
        public void DeletingRemovesJustThatComment()
        {
            using var doc = MakeDoc(("Text", "first", "A"), ("Text", "second", "B"),
                                    ("Text", "third", "C"));
            var all = PdfComments.Read(doc);
            var middle = all.Single(c => c.Contents == "second");

            Assert.Equal(1, PdfComments.Delete(doc, [middle]));

            var left = PdfComments.Read(doc).Select(c => c.Contents).ToArray();
            Assert.Equal(["first", "third"], left);
        }

        [Fact]
        public void DeletingSeveralAtOnceRemovesExactlyThose()
        {
            // The index-shifting case: deleting in the caller's order would take the wrong ones
            // after the first removal.
            using var doc = MakeDoc(("Text", "one", "A"), ("Text", "two", "B"),
                                    ("Text", "three", "C"), ("Text", "four", "D"));
            var all = PdfComments.Read(doc);
            var doomed = all.Where(c => c.Contents is "one" or "three").ToList();

            Assert.Equal(2, PdfComments.Delete(doc, doomed));

            var left = PdfComments.Read(doc).Select(c => c.Contents).ToArray();
            Assert.Equal(["two", "four"], left);
        }

        [Fact]
        public void EditsSurviveASaveAndReopen()
        {
            string path = Path.Combine(_dir, "roundtrip.pdf");
            using (var doc = MakeDoc(("Text", "before", "A"), ("Text", "doomed", "B")))
            {
                var all = PdfComments.Read(doc);
                PdfComments.UpdateContents(doc, all.Single(c => c.Contents == "before"), "after");
                PdfComments.Delete(doc, [all.Single(c => c.Contents == "doomed")]);
                PdfSaveGuard.Save(doc, path);
            }

            var reopened = PdfComments.Read(path);
            var only = Assert.Single(reopened);
            Assert.Equal("after", only.Contents);
        }

        [Fact]
        public void AcommentCarriesEnoughToFindItAgain()
        {
            using var doc = MakeDoc(("Text", "note", "A"));
            var c = PdfComments.Read(doc).Single();
            Assert.Equal(0, c.PageIndex);
            Assert.Equal(0, c.AnnotIndex);
        }

        [Fact]
        public void AstaleLocatorIsRefusedRatherThanEditingTheWrongAnnotation()
        {
            // A comment held from before an unrelated edit must not be applied blindly: the slot
            // it names may now hold a different annotation.
            using var doc = MakeDoc(("Text", "note", "A"));
            var stale = PdfComments.Read(doc).Single() with { Kind = "Highlight" };

            Assert.False(PdfComments.UpdateContents(doc, stale, "should not land"));
            Assert.Equal(0, PdfComments.Delete(doc, [stale]));
            Assert.Equal("note", PdfComments.Read(doc).Single().Contents);
        }

        [Fact]
        public void OutOfRangeLocatorsAreHarmless()
        {
            using var doc = MakeDoc(("Text", "note", "A"));
            var bogus = new PdfComment(9, "Text", "A", "x", "", false, PageIndex: 8, AnnotIndex: 7);

            Assert.False(PdfComments.UpdateContents(doc, bogus, "nope"));
            Assert.False(PdfComments.UpdateAuthor(doc, bogus, "nope"));
            Assert.Equal(0, PdfComments.Delete(doc, [bogus]));
            Assert.Single(PdfComments.Read(doc));
        }

        [Fact]
        public void DeletingNothingIsSafe()
        {
            using var doc = MakeDoc(("Text", "note", "A"));
            Assert.Equal(0, PdfComments.Delete(doc, []));
            Assert.Single(PdfComments.Read(doc));
        }
    }
}
