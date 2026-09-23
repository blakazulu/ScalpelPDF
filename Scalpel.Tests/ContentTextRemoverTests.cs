using System;
using System.IO;
using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Removing the original words when text is edited.
    ///
    /// <para>Painting an opaque cover over the old text hides it on screen but leaves it in the
    /// content stream, so any text extractor returns the original alongside the replacement. These
    /// tests pin both halves of the contract: the text really is gone, and a stream this cannot
    /// confidently edit is left completely untouched rather than damaged.</para>
    /// </summary>
    public class ContentTextRemoverTests
    {
        private static byte[] Bytes(string s) => Encoding.GetEncoding(28591).GetBytes(s);
        private static string Str(byte[] b) => Encoding.GetEncoding(28591).GetString(b);

        [Fact]
        public void ASimpleShownStringIsCleared()
        {
            byte[] stream = Bytes("BT /F1 12 Tf 20 700 Td (SALARY-120000) Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "SALARY-120000", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            string text = Str(edited!);
            Assert.DoesNotContain("SALARY", text);
            // The operator and its surroundings must survive untouched.
            Assert.Contains("() Tj", text);
            Assert.Contains("BT", text);
            Assert.Contains("ET", text);
        }

        [Fact]
        public void AWordSplitAcrossSeveralOperandsIsFullyCleared()
        {
            // Kerning routinely splits one visible word into several show operations.
            byte[] stream = Bytes("BT [(SEC) -20 (RET) -15 (-VALUE)] TJ ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "SECRET-VALUE", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(3, cleared);
            string text = Str(edited!);
            Assert.DoesNotContain("SEC", text);
            Assert.DoesNotContain("RET", text);
            Assert.Contains("TJ", text);
        }

        [Fact]
        public void OnlyTheMatchingRunIsRemoved()
        {
            byte[] stream = Bytes("BT (KEEP THIS) Tj (SECRET) Tj (AND THIS) Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "SECRET", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            string text = Str(edited!);
            Assert.Contains("KEEP THIS", text);
            Assert.Contains("AND THIS", text);
            Assert.DoesNotContain("SECRET", text);
        }

        [Fact]
        public void AMatchInsideALongerOperandIsNotRemoved()
        {
            // Operands are emptied whole, so matching "TOTAL 100" inside "(SUBTOTAL 100)" would
            // delete a line the user never edited.
            byte[] stream = Bytes("BT (SUBTOTAL 100) Tj 0 -14 Td (TOTAL 100) Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "TOTAL 100", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            string text = Str(edited!);
            Assert.Contains("(SUBTOTAL 100)", text);
            Assert.DoesNotContain("(TOTAL 100)", text);
        }

        [Fact]
        public void ATargetThatOnlyAppearsInsideAnotherWordIsLeftAlone()
        {
            byte[] stream = Bytes("BT (Invoice 2100) Tj ET");

            Assert.Null(ContentTextRemover.RemoveFromStream(stream, "100", out int cleared));
            Assert.Equal(0, cleared);
        }

        [Fact]
        public void TwoIdenticalLinesAreAmbiguous_SoNeitherIsRemoved()
        {
            // Without positions there is no telling which "Name: Dan" was edited; guessing
            // deletes the wrong one. The caller keeps the cover as its fallback.
            byte[] stream = Bytes("BT (Name: Dan) Tj 0 -14 Td (Name: Dan) Tj ET");

            Assert.Null(ContentTextRemover.RemoveFromStream(stream, "Name: Dan", out int cleared));
            Assert.Equal(0, cleared);
        }

        [Fact]
        public void CountMatches_CountsOnlyWholeOperandRuns()
        {
            Assert.Equal(2, ContentTextRemover.CountMatches(Bytes("BT (A) Tj (A) Tj (AB) Tj ET"), "A"));
            Assert.Equal(1, ContentTextRemover.CountMatches(Bytes("BT [(SEC) 5 (RET)] TJ ET"), "SECRET"));
            Assert.Equal(0, ContentTextRemover.CountMatches(Bytes("BT (SECRETS) Tj ET"), "SECRET"));
        }

        [Fact]
        public void HexStringsAreUnderstood()
        {
            // "AB" written as a hex string operand.
            byte[] stream = Bytes("BT <4142> Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "AB", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            Assert.DoesNotContain("4142", Str(edited!));
        }

        [Fact]
        public void EscapedCharactersAreDecodedBeforeMatching()
        {
            // A literal string carrying an escaped bracket.
            byte[] stream = Bytes(@"BT (CALL \(URGENT\)) Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "CALL (URGENT)", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            Assert.DoesNotContain("URGENT", Str(edited!));
        }

        [Fact]
        public void WhitespaceDifferencesDoNotPreventAMatch()
        {
            // The viewer shows "Total Due" as one phrase; the stream may space it differently.
            byte[] stream = Bytes("BT (Total  Due) Tj ET");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "Total Due", out int cleared);

            Assert.NotNull(edited);
            Assert.Equal(1, cleared);
            Assert.DoesNotContain("Total", Str(edited!));
        }

        [Fact]
        public void TextThatIsNotThereLeavesTheStreamCompletelyUntouched()
        {
            // The safety rule: no confident match means no edit at all. This is the subset-font
            // case, where the bytes in the file are not the characters on the page.
            byte[] stream = Bytes("BT (\\001\\002\\003) Tj ET");
            byte[] original = (byte[])stream.Clone();

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "SECRET", out int cleared);

            Assert.Null(edited);
            Assert.Equal(0, cleared);
            Assert.Equal(original, stream);
        }

        [Fact]
        public void AStringThatIsNotShownTextIsNotTouched()
        {
            // A string operand belonging to something other than a text operator - here a marked
            // content property - must not be mistaken for shown text.
            byte[] stream = Bytes("/Span <</ActualText (SECRET)>> BDC (visible) Tj EMC");

            byte[]? edited = ContentTextRemover.RemoveFromStream(stream, "SECRET", out int cleared);

            Assert.Null(edited);
            Assert.Equal(0, cleared);
        }

        [Fact]
        public void EmptyAndNullInputsAreHarmless()
        {
            Assert.Null(ContentTextRemover.RemoveFromStream([], "x", out int a));
            Assert.Equal(0, a);
            Assert.Null(ContentTextRemover.RemoveFromStream(Bytes("BT (x) Tj ET"), "", out int b));
            Assert.Equal(0, b);
        }

        [Fact]
        public void TheOriginalIsNoLongerExtractableFromARealSavedPdf()
        {
            // The end-to-end property the whole thing exists for.
            string path = Path.Combine(Path.GetTempPath(),
                "ctr-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
            try
            {
                // Build a document and save it first. This mirrors the real case - the user edits
                // text in a PDF opened from disk - and it matters technically too: a font's
                // ToUnicode map, which is what turns glyph codes back into readable characters,
                // is only written when the document is serialized.
                string source = Path.Combine(Path.GetTempPath(),
                    "ctr-src-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(300);
                    page.Height = XUnit.FromPoint(200);
                    using var gfx = XGraphics.FromPdfPage(page);
                    var font = new XFont("Arial", 14);
                    gfx.DrawString("SALARY-120000-SECRET", font, XBrushes.Black, 20, 60);
                    gfx.DrawString("KEEP-THIS-LINE", font, XBrushes.Black, 20, 100);
                    PdfSaveGuard.Save(doc, source);
                }

                try
                {
                    using var opened = PdfSharpCore.Pdf.IO.PdfReader.Open(
                        source, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);

                    int cleared = ContentTextRemover.RemoveText(opened.Pages[0], "SALARY-120000-SECRET");
                    Assert.True(cleared > 0, "the original run was not found in the content stream");

                    PdfSaveGuard.Save(opened, path);
                }
                finally { try { File.Delete(source); } catch { } }

                using var pig = UglyToad.PdfPig.PdfDocument.Open(path);
                string text = pig.GetPage(1).Text ?? "";

                Assert.DoesNotContain("SECRET", text);
                Assert.Contains("KEEP-THIS-LINE", text);
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void ThePageStillOpensAfterTheEdit()
        {
            // Emptying operands must never leave a stream a parser rejects.
            string path = Path.Combine(Path.GetTempPath(),
                "ctr2-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
            try
            {
                using (var doc = new PdfDocument())
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var page = doc.AddPage();
                        page.Width = XUnit.FromPoint(300);
                        page.Height = XUnit.FromPoint(200);
                        using var gfx = XGraphics.FromPdfPage(page);
                        gfx.DrawString("REMOVE-ME", new XFont("Arial", 14), XBrushes.Black, 20, 60);
                    }
                    ContentTextRemover.RemoveText(doc.Pages[1], "REMOVE-ME");
                    PdfSaveGuard.Save(doc, path);
                }

                using var reopened = PdfSharpCore.Pdf.IO.PdfReader.Open(
                    path, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify);
                Assert.Equal(3, reopened.PageCount);
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
