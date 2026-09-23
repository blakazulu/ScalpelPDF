using Scalpel.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.PdfFonts;
using Xunit;

namespace Scalpel.Tests;

// Column-aware reading order over synthetic PdfPig words. Coordinates are PDF space
// (points, bottom-left origin): a larger Y is higher on the page.
public sealed class TextRunServiceTests
{
    private const double LineHeight = 10;
    private const double CharWidth = 6;

    [Theory]
    [InlineData("English text", false)]
    [InlineData("متن فارسی", true)]
    [InlineData("نص عربي", true)]
    [InlineData("טקסט עברי", true)]
    [InlineData("1234", false)]
    public void DetectsLineDirection(string text, bool expected)
        => Assert.Equal(expected, TextRunService.IsRightToLeftText([text]));

    [Fact]
    public void SingleColumn_ReadsTopToBottomLeftToRight_RegardlessOfInputOrder()
    {
        var words = new List<Word>();
        words.AddRange(Line(660, 50, "five", "six"));
        words.AddRange(Line(700, 50, "one", "two"));
        words.AddRange(Line(680, 50, "three", "four"));
        words.Reverse();

        var ordered = TextRunService.OrderColumnAware(words);

        Assert.Equal(["one", "two", "three", "four", "five", "six"], ordered.Select(w => w.Text));
    }

    [Fact]
    public void TwoColumns_ReadWholeLeftColumnBeforeRightColumn()
    {
        var words = new List<Word>();
        for (int row = 0; row < 3; row++)
        {
            double y = 700 - row * 20;
            words.AddRange(Line(y, 50, $"L{row}a", $"L{row}b"));
            words.AddRange(Line(y, 300, $"R{row}a", $"R{row}b"));
        }

        var ordered = TextRunService.OrderColumnAware(words);

        Assert.Equal(
            ["L0a", "L0b", "L1a", "L1b", "L2a", "L2b", "R0a", "R0b", "R1a", "R1b", "R2a", "R2b"],
            ordered.Select(w => w.Text));
    }

    [Fact]
    public void WideTitleAndFooter_BracketTheColumnSection()
    {
        var words = new List<Word>();
        words.Add(Word("Title", 50, 760, 450));   // spans the whole text width
        for (int row = 0; row < 2; row++)
        {
            double y = 700 - row * 20;
            words.AddRange(Line(y, 50, $"L{row}"));
            words.AddRange(Line(y, 300, $"R{row}"));
        }
        words.Add(Word("Footer", 50, 100, 450));

        var ordered = TextRunService.OrderColumnAware(words);

        Assert.Equal(["Title", "L0", "L1", "R0", "R1", "Footer"], ordered.Select(w => w.Text));
    }

    [Fact]
    public void HebrewLine_OrdersWordsRightToLeft()
    {
        // Visually the first Hebrew word sits at the right edge of the line.
        var right = Word("שלום", 100, 700, 124);
        var left = Word("עולם", 60, 700, 84);

        var bands = TextRunService.OrderBands([left, right]);

        var band = Assert.Single(bands);
        Assert.True(band.RightToLeft);
        Assert.Equal(["שלום", "עולם"], band.Words.Select(w => w.Text));
    }

    [Fact]
    public void GroupIntoLines_JoinsWordsThatOverlapVertically()
    {
        var a = Word("a", 50, 700, 56);
        var b = Word("b", 70, 703, 76);     // overlaps a's band by 70%
        var c = Word("c", 50, 680, 56);     // a separate line

        var lines = TextRunService.GroupIntoLines([a, b, c]);

        Assert.Equal(2, lines.Count);
        Assert.Equal(["a", "b"], lines[0].Words.Select(w => w.Text));
        Assert.Equal(["c"], lines[1].Words.Select(w => w.Text));
        Assert.Equal(713, lines[0].Top, 6);
        Assert.Equal(700, lines[0].Bottom, 6);
    }

    [Fact]
    public void EmptyInput_YieldsEmptyOrder()
    {
        Assert.Empty(TextRunService.OrderColumnAware([]));
    }

    /// <summary>Lays out <paramref name="texts"/> left-to-right on one line starting at
    /// <paramref name="x"/>, one word space apart.</summary>
    private static IEnumerable<Word> Line(double y, double x, params string[] texts)
    {
        foreach (string text in texts)
        {
            double right = x + text.Length * CharWidth;
            yield return Word(text, x, y, right);
            x = right + CharWidth;
        }
    }

    private static Word Word(string text, double left, double bottom, double right)
    {
        var rect = new PdfRectangle(left, bottom, right, bottom + LineHeight);
        var letter = new Letter(text, rect, rect,
            new PdfPoint(left, bottom), new PdfPoint(right, bottom),
            right - left, LineHeight,
            new FontDetails("Test", false, FontDetails.DefaultWeight, false),
            TextRenderingMode.Fill, GrayColor.Black, GrayColor.Black, LineHeight, 0);
        return new Word([letter]);
    }
}
