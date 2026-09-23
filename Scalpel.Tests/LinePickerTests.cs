using System.Collections.Generic;
using Scalpel.Services;
using Xunit;
using W = Scalpel.Services.LinePicker.WordBox;

namespace Scalpel.Tests
{
    public class LinePickerTests
    {
        // Canvas space, y down. Two lines of 12px-high words, 20px apart.
        private static readonly List<W> TwoLines =
        [
            new(10, 100, 50, 112),  // 0 line 1
            new(55, 100, 90, 112),  // 1 line 1
            new(95, 100, 140, 112), // 2 line 1
            new(10, 120, 60, 132),  // 3 line 2
            new(65, 120, 99, 132),  // 4 line 2
        ];

        [Fact]
        public void DoubleClickAnywhereOnALine_PicksTheWholeLine()
        {
            Assert.Equal([0, 1, 2], LinePicker.Pick(TwoLines, 120, 106));
            Assert.Equal([0, 1, 2], LinePicker.Pick(TwoLines, 12, 101));   // near the top edge
            Assert.Equal([3, 4], LinePicker.Pick(TwoLines, 80, 130));
        }

        [Fact]
        public void ASmallMarkOnTheLine_IsIncludedEvenIfTheClickMissesItsBox()
        {
            // A hyphen: a thin box in the middle of the line. It sits under the white cover, so
            // leaving it out of the edit would delete it.
            var words = new List<W>(TwoLines) { new(142, 105, 148, 107) };
            Assert.Equal([0, 1, 2, 5], LinePicker.Pick(words, 20, 101));
        }

        [Fact]
        public void TwoColumns_AreNotMerged()
        {
            // Left column ends at x=140, right column starts at x=300: a gutter of many ems.
            var words = new List<W>(TwoLines) { new(300, 100, 340, 112), new(345, 100, 380, 112) };
            Assert.Equal([0, 1, 2], LinePicker.Pick(words, 60, 106));
            Assert.Equal([5, 6], LinePicker.Pick(words, 360, 106));
        }

        [Fact]
        public void JustifiedSpacing_KeepsTheLineWhole()
        {
            // Gaps of 20px on a 12px line (under 2.5em): still one line.
            var words = new List<W> { new(0, 0, 40, 12), new(60, 0, 100, 12), new(120, 0, 160, 12) };
            Assert.Equal([0, 1, 2], LinePicker.Pick(words, 130, 6));
        }

        [Fact]
        public void AClickFarFromAnyText_PicksNothing()
        {
            Assert.Empty(LinePicker.Pick(TwoLines, 50, 400));
        }

        [Fact]
        public void AClickJustOffALine_SnapsToIt()
        {
            Assert.Equal([3, 4], LinePicker.Pick(TwoLines, 30, 139));
        }

        [Fact]
        public void BetweenTwoTightLines_TheCloserLineWins()
        {
            var words = new List<W> { new(0, 0, 40, 12), new(0, 13, 40, 25) };
            Assert.Equal([0], LinePicker.Pick(words, 10, 11));
            Assert.Equal([1], LinePicker.Pick(words, 10, 14));
        }

        [Fact]
        public void NoWords_PicksNothing() => Assert.Empty(LinePicker.Pick([], 0, 0));
    }
}
