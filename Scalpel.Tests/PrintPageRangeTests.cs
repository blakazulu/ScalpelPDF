using System.Diagnostics;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class PrintPageRangeTests
    {
        [Fact]
        public void BlankRangeMeansEveryPage()
            => Assert.Equal([0, 1, 2, 3], PrintPageRange.Parse("", 4));

        [Fact]
        public void NullRangeMeansEveryPage()
            => Assert.Equal([0, 1], PrintPageRange.Parse(null, 2));

        [Theory]
        [InlineData("1", new[] { 0 })]
        [InlineData("2,4", new[] { 1, 3 })]
        [InlineData("1-3", new[] { 0, 1, 2 })]
        [InlineData("3-1", new[] { 0, 1, 2 })]          // reversed range is normalised
        [InlineData(" 1 , 3 - 4 ", new[] { 0, 2, 3 })]  // whitespace tolerated
        [InlineData("2,2,2", new[] { 1 })]              // duplicates collapse
        public void ParsesOrdinaryRanges(string text, int[] expected)
            => Assert.Equal(expected, PrintPageRange.Parse(text, 6));

        [Theory]
        [InlineData("3-", new[] { 2, 3 })]
        [InlineData("-2", new[] { 0, 1 })]
        public void OpenEndedRangesReachTheDocumentBounds(string text, int[] expected)
            => Assert.Equal(expected, PrintPageRange.Parse(text, 4));

        [Fact]
        public void OutOfRangePagesAreDroppedNotClampedIn()
            => Assert.Equal([1], PrintPageRange.Parse("2,99", 3));

        [Fact]
        public void AnUnmatchedRangeReturnsNothingRatherThanEveryPage()
        {
            // The old behaviour fell back to all pages here, so a typo printed the document.
            Assert.Empty(PrintPageRange.Parse("99", 3));
            Assert.Empty(PrintPageRange.Parse("50-80", 3));
            Assert.Empty(PrintPageRange.Parse("abc", 3));
        }

        [Fact]
        public void AHugeUpperBoundIsClampedAndReturnsImmediately()
        {
            var sw = Stopwatch.StartNew();
            var pages = PrintPageRange.Parse("1-2147483647", 5);
            sw.Stop();

            Assert.Equal([0, 1, 2, 3, 4], pages);
            Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void AHugeBoundBeyondIntRangeIsAlsoSafe()
        {
            var pages = PrintPageRange.Parse("1-99999999999999", 3);
            Assert.Equal([0, 1, 2], pages);
        }

        [Theory]
        [InlineData(PrintSubset.Odd, new[] { 0, 2, 4 })]
        [InlineData(PrintSubset.Even, new[] { 1, 3 })]
        [InlineData(PrintSubset.All, new[] { 0, 1, 2, 3, 4 })]
        public void SubsetFiltersByPrintedPageNumber(PrintSubset subset, int[] expected)
            => Assert.Equal(expected, PrintPageRange.Parse("", 5, subset));

        [Fact]
        public void SubsetAppliesInsideAnExplicitRange()
            => Assert.Equal([1, 3], PrintPageRange.Parse("2-4", 6, PrintSubset.Even));

        [Fact]
        public void EmptyDocumentYieldsNothing()
            => Assert.Empty(PrintPageRange.Parse("1-3", 0));

        [Theory]
        [InlineData(4, 1, false, 4)]
        [InlineData(4, 1, true, 2)]
        [InlineData(5, 1, true, 3)]   // odd page count rounds up
        [InlineData(4, 3, false, 12)]
        [InlineData(0, 2, false, 0)]
        public void SheetCountAccountsForDuplexAndCopies(int pages, int copies, bool duplex, int expected)
            => Assert.Equal(expected, PrintPageRange.SheetCount(pages, copies, duplex));
    }
}
