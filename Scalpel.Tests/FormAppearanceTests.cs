using System;
using System.Globalization;
using System.Threading;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class FormAppearanceTests
    {
        private static T InCulture<T>(string culture, Func<T> body)
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                return body();
            }
            finally { Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("tr-TR")]
        [InlineData("ru-RU")]
        public void NumbersAreInvariantOnCommaDecimalCultures(string culture)
        {
            var content = InCulture(culture, () =>
                FormAppearance.BuildTextFieldContent("Hello", "/Helv", 10.5, 120.25, 18.75, multiline: false));

            Assert.DoesNotContain(",", content);
            Assert.Contains("10.5 Tf", content);
            Assert.Contains("120.25 18.75 re W n", content);
        }

        [Fact]
        public void SingleLineCentresBaselineAndEmitsOneTj()
        {
            var content = FormAppearance.BuildTextFieldContent("Hello", "/Helv", 10, 100, 20, multiline: false);

            Assert.Contains("(Hello) Tj", content);
            Assert.Equal(1, CountOccurrences(content, ") Tj"));
            Assert.DoesNotContain("T*", content);
            Assert.StartsWith("/Tx BMC", content);
            Assert.EndsWith("EMC", content);
        }

        [Fact]
        public void MultiLineEmitsLeadingAndOneRunPerLine()
        {
            var content = FormAppearance.BuildTextFieldContent("one\r\ntwo\nthree", "/Helv", 10, 100, 60, multiline: true);

            Assert.Contains("11.6 TL", content);          // 10 * LineHeightFactor
            Assert.Contains("(one) Tj", content);
            Assert.Contains("(two) Tj", content);
            Assert.Contains("(three) Tj", content);
            Assert.Equal(2, CountOccurrences(content, "T*"));
        }

        [Fact]
        public void SingleLineFieldFlattensNewlinesInsteadOfBreakingTheStream()
        {
            var content = FormAppearance.BuildTextFieldContent("a\nb", "/Helv", 10, 100, 20, multiline: false);

            Assert.Contains("(a b) Tj", content);
            Assert.DoesNotContain("\\n", content);
        }

        [Fact]
        public void KeepsWindows1252CharactersInsteadOfReplacingThem()
        {
            // Smart quote, en dash and euro all exist in Windows-1252 and used to become '?'.
            var escaped = FormAppearance.EscapePdfString("it’s – €5");
            Assert.Equal("it’s – €5", escaped);

            var bytes = FormAppearance.EncodeContent("’–€");
            Assert.Equal([0x92, 0x96, 0x80], bytes);
        }

        [Fact]
        public void ReplacesCharactersOutsideWindows1252()
        {
            Assert.Equal("??", FormAppearance.EscapePdfString("א中"));  // Hebrew alef, CJK
        }

        [Fact]
        public void EscapesPdfSyntaxCharacters()
        {
            Assert.Equal(@"a\(b\)c\\d", FormAppearance.EscapePdfString(@"a(b)c\d"));
        }

        [Fact]
        public void SplitLinesHandlesEveryLineEnding()
        {
            Assert.Equal(["a", "b", "c"], FormAppearance.SplitLines("a\r\nb\rc"));
            Assert.Equal([""], FormAppearance.SplitLines(null));
        }

        [Fact]
        public void NumRejectsNonFiniteValues()
        {
            Assert.Equal("0", FormAppearance.Num(double.NaN));
            Assert.Equal("0", FormAppearance.Num(double.PositiveInfinity));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
