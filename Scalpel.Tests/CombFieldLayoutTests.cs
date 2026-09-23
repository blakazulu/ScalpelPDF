using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class CombFieldLayoutTests
{
    [Fact]
    public void CharactersOccupyDistinctEqualCells()
    {
        Assert.Equal(0, CombFieldLayout.CellLeft(120, 4, 0));
        Assert.Equal(30, CombFieldLayout.CellLeft(120, 4, 1));
        Assert.Equal(60, CombFieldLayout.CellLeft(120, 4, 2));
        Assert.Equal(90, CombFieldLayout.CellLeft(120, 4, 3));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(29.9, 0)]
    [InlineData(30, 1)]
    [InlineData(89.9, 2)]
    [InlineData(120, 3)]
    public void ClickMapsToVisibleCell(double x, int expected)
    {
        Assert.Equal(expected, CombFieldLayout.CellIndexAt(x, 120, 4));
    }
}
