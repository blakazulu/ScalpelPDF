using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class BidiReorderTests
    {
        // shalom = \u05E9\u05DC\u05D5\u05DD  (logical: shin lamed vav mem)
        private const string Shalom = "\u05E9\u05DC\u05D5\u05DD";
        // visual (reversed): \u05DD\u05D5\u05DC\u05E9  (mem vav lamed shin)
        private const string ShalomVisual = "\u05DD\u05D5\u05DC\u05E9";

        [Theory]
        [InlineData("hello", false)]
        [InlineData("", false)]
        [InlineData("\u05E9\u05DC\u05D5\u05DD", true)]
        [InlineData("abc \u05E9\u05DC", true)]
        public void ContainsRtl_Detects(string s, bool expected)
            => Assert.Equal(expected, BidiReorder.ContainsRtl(s));

        [Fact]
        public void ToVisual_PureLatin_Unchanged()
            => Assert.Equal("hello world", BidiReorder.ToVisual("hello world"));

        [Fact]
        public void ToVisual_PureHebrew_Reversed()
            => Assert.Equal(ShalomVisual, BidiReorder.ToVisual(Shalom));

        [Fact]
        public void ToVisual_HebrewThenNumber_NumberStaysLtrOnLeft()
        {
            // logical: "shalom 123"  -> visual: "123 " + reversed shalom
            string logical = Shalom + " 123";
            string expected = "123 " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_HebrewThenLatinWord_LatinForwardOnLeft()
        {
            // logical: "shalom world" -> visual: "world " + reversed shalom
            string logical = Shalom + " world";
            string expected = "world " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_Empty_ReturnsEmpty()
            => Assert.Equal("", BidiReorder.ToVisual(""));

        [Fact]
        public void ToVisual_HebrewWithPunctuation_ContainsExpectedSubstrings()
        {
            // "\u05E9\u05DC\u05D5\u05DD, world." -- base RTL: Hebrew run reversed, Latin forward;
            // neutral punctuation (comma, period) resolves to RTL and may separate from Latin run;
            // assert robust invariants: Latin word present, reversed shalom present, total length preserved
            var result = BidiReorder.ToVisual(Shalom + ", world.");
            Assert.Contains("world", result); // Latin run appears intact (trailing . reorders as neutral)
            Assert.Contains(ShalomVisual, result); // reversed shalom: \u05DD\u05D5\u05DC\u05E9
            Assert.Equal((Shalom + ", world.").Length, result.Length);
        }

        [Fact]
        public void ToVisual_HebrewWithTrailingDigits_ReversedWithDigitsLeading()
        {
            // "\u05E9\u05DC\u05D5\u05DD 42" -> "42 \u05DD\u05D5\u05DC\u05E9"
            Assert.Equal("42 " + ShalomVisual, BidiReorder.ToVisual(Shalom + " 42"));
        }

        // --- Arabic ---------------------------------------------------------
        // salam = U+0633 U+0644 U+0627 U+0645 (logical: seen lam alef meem)
        private const string Salam = "\u0633\u0644\u0627\u0645";

        private static string Reverse(string s)
        {
            char[] a = s.ToCharArray();
            System.Array.Reverse(a);
            return new string(a);
        }

        [Fact]
        public void ContainsRtl_DetectsArabic()
            => Assert.True(BidiReorder.ContainsRtl(Salam));

        [Fact]
        public void ContainsRtl_DetectsArabicPresentationForms()
            => Assert.True(BidiReorder.ContainsRtl("\uFE91\uFE92")); // BEH initial+medial (Forms-B)

        [Fact]
        public void ToVisual_PureArabic_Reversed()
            => Assert.Equal(Reverse(Salam), BidiReorder.ToVisual(Salam));

        [Fact]
        public void ToVisual_ArabicThenDigits_DigitsStayLtrOnLeft()
            => Assert.Equal("42 " + Reverse(Salam), BidiReorder.ToVisual(Salam + " 42"));

        // --- JoinWordsLogical: reconstruct a logical-order line from positioned words ----------
        // olam (logical: ayin vav lamed final-mem) + its VISUAL (reversed) form, as a PDF text
        // extractor returns it.
        private const string Olam = "\u05E2\u05D5\u05DC\u05DD";
        private const string OlamVisual = "\u05DD\u05DC\u05D5\u05E2";

        [Fact]
        public void JoinWordsLogical_Hebrew_RebuildsLogicalLine()
        {
            // As PdfPig returns "shalom olam": words left-to-right (olam at smaller x, shalom at
            // larger x), each word's chars in VISUAL order. Reconstruction must yield the logical
            // line "shalom olam" (words right-to-left, each RTL word un-reversed).
            var words = new (string, double)[] { (OlamVisual, 100.0), (ShalomVisual, 300.0) };
            Assert.Equal(Shalom + " " + Olam, BidiReorder.JoinWordsLogical(words));
        }

        [Fact]
        public void JoinWordsLogical_English_KeepsLeftToRight()
        {
            var words = new (string, double)[] { ("hello", 100.0), ("world", 200.0) };
            Assert.Equal("hello world", BidiReorder.JoinWordsLogical(words));
        }

        [Fact]
        public void JoinWordsLogical_SingleHebrewWord_RebuildsLogical()
        {
            // A single Hebrew word arrives visual (reversed); reconstruction restores logical.
            var words = new (string, double)[] { (ShalomVisual, 50.0) };
            Assert.Equal(Shalom, BidiReorder.JoinWordsLogical(words));
        }

        [Fact]
        public void JoinWordsLogical_Empty_ReturnsEmpty()
            => Assert.Equal("", BidiReorder.JoinWordsLogical(System.Array.Empty<(string, double)>()));
    }
}
