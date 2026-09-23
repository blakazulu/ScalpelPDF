using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// A comb field is ruled into equal cells with one character per cell. The appearance stream
    /// is what every other PDF reader draws, so getting the value into the cells matters as much
    /// as Scalpel's own overlay.
    /// </summary>
    public class CombAppearanceTests
    {
        private static string Build(string text, int cells, double width = 120, double height = 20)
            => FormAppearance.BuildTextFieldContent(text, "/Helv", 10, width, height,
                                                    multiline: false, combCells: cells);

        [Fact]
        public void EachCharacterIsPositionedSeparately()
        {
            string content = Build("AB12", 4);

            // One text-matrix placement per character, rather than a single run.
            Assert.Equal(4, CountOccurrences(content, " Tm"));
            Assert.Equal(4, CountOccurrences(content, ") Tj"));
        }

        [Fact]
        public void CharactersAdvanceAcrossTheField()
        {
            // Cell centres must increase left to right; a constant x would stack them.
            string content = Build("ABCD", 4, width: 200);
            var xs = ExtractPlacementXs(content);

            Assert.Equal(4, xs.Count);
            for (int i = 1; i < xs.Count; i++)
                Assert.True(xs[i] > xs[i - 1], $"placement {i} did not advance: {xs[i - 1]} then {xs[i]}");
        }

        [Fact]
        public void ValueLongerThanTheCombIsTruncatedToTheCells()
        {
            string content = Build("ABCDEFGH", 3);
            Assert.Equal(3, CountOccurrences(content, ") Tj"));
        }

        [Fact]
        public void AShortValueOnlyDrawsWhatIsThere()
        {
            string content = Build("A", 6);
            Assert.Equal(1, CountOccurrences(content, ") Tj"));
        }

        [Fact]
        public void AnEmptyCombDrawsNoGlyphs()
        {
            string content = Build("", 5);
            Assert.Equal(0, CountOccurrences(content, ") Tj"));
            // The stream must still be well formed so the field is not left without one.
            Assert.Contains("/Tx BMC", content);
            Assert.Contains("EMC", content);
        }

        [Fact]
        public void WithoutCombCellsTheFieldStillWritesOneRun()
        {
            // Regression guard: the comb path must not capture ordinary text fields.
            string content = FormAppearance.BuildTextFieldContent(
                "hello", "/Helv", 10, 120, 20, multiline: false, combCells: 0);

            Assert.Equal(1, CountOccurrences(content, ") Tj"));
            Assert.Contains("(hello) Tj", content);
        }

        [Fact]
        public void CombNumbersUseTheInvariantDecimalSeparator()
        {
            // The whole reason FormAppearance exists: a comma separator makes the stream invalid.
            string content = Build("AB", 2, width: 33.5, height: 11.25);
            Assert.DoesNotContain(",", content);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, index = 0;
            while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
            { count++; index += needle.Length; }
            return count;
        }

        /// <summary>Pulls the x of each "1 0 0 1 x y Tm" placement, in order.</summary>
        private static System.Collections.Generic.List<double> ExtractPlacementXs(string content)
        {
            var xs = new System.Collections.Generic.List<double>();
            foreach (var line in content.Split('\n'))
            {
                string t = line.Trim();
                if (!t.StartsWith("1 0 0 1 ", System.StringComparison.Ordinal) ||
                    !t.EndsWith("Tm", System.StringComparison.Ordinal)) continue;
                var parts = t.Split(' ');
                if (parts.Length >= 6 && double.TryParse(parts[4],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double x))
                    xs.Add(x);
            }
            return xs;
        }
    }
}
