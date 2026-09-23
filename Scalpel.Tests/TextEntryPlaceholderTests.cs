using System.Windows;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class TextEntryPlaceholderTests
{
    [Theory]
    [InlineData(".................")]
    [InlineData("____________")]
    [InlineData("\u00B7\u00B7\u00B7\u00B7\u00B7\u00B7")]
    [InlineData("------")]
    public void RecognizesFlattenedFormEntryRuns(string text) =>
        Assert.True(TextEntryPlaceholder.IsPlaceholder(text));

    [Theory]
    [InlineData("Name: ........")]
    [InlineData("file.pdf")]
    [InlineData("...")]
    [InlineData("hello")]
    public void RejectsOrdinaryText(string text) =>
        Assert.False(TextEntryPlaceholder.IsPlaceholder(text));

    [Fact]
    public void FindsClickedPlaceholderWithoutSelectingNearbyLabel()
    {
        var label = new TextEntryPlaceholder.Candidate("Name:", new Rect(20, 40, 45, 12));
        var blank = new TextEntryPlaceholder.Candidate(".............", new Rect(75, 40, 130, 12));

        Rect found = Assert.IsType<Rect>(TextEntryPlaceholder.FindNearest([label, blank], new Point(120, 55)));

        Assert.Equal(blank.Bounds, found);
    }
}
