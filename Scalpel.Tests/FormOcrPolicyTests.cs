using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class FormOcrPolicyTests
{
    [Fact]
    public void MapRegions_MapsPdfCoordinatesAndTextConstraints()
    {
        FormOcrWidget widget = Widget("totalAmount", FormOcrFieldKind.Text,
            left: 10, bottom: 20, right: 60, top: 40, maximumLength: 8);

        FormOcrPolicy.Region region = Assert.Single(
            FormOcrPolicy.MapRegions([widget], 200, 100));

        Assert.Equal((20, 60, 120, 80),
            (region.Left, region.Top, region.Right, region.Bottom));
        Assert.Equal(FormOcrPolicy.NumericWhitelist, region.CharacterWhitelist);
        Assert.Equal(8, region.MaximumLength);
    }

    [Fact]
    public void MapRegions_AppliesVisualPageRotation()
    {
        FormOcrWidget widget = Widget("name", FormOcrFieldKind.Text,
            left: 10, bottom: 20, right: 60, top: 40, pageRotation: 90);

        FormOcrPolicy.Region region = Assert.Single(
            FormOcrPolicy.MapRegions([widget], 100, 200));

        Assert.Equal((20, 20, 40, 120),
            (region.Left, region.Top, region.Right, region.Bottom));
    }

    [Fact]
    public void MapRegions_SkipsNonTextWidgetsAndTinyBoxes()
    {
        FormOcrWidget button = Widget("submit", FormOcrFieldKind.Other,
            left: 10, bottom: 20, right: 60, top: 40);
        FormOcrWidget sliver = Widget("tiny", FormOcrFieldKind.Text,
            left: 10, bottom: 20, right: 10.5, top: 40);

        Assert.Empty(FormOcrPolicy.MapRegions([button, sliver], 200, 100));
    }

    [Fact]
    public void MapRegions_KeepsChoiceValuesForListFields()
    {
        FormOcrWidget widget = Widget("subject", FormOcrFieldKind.Choice,
            left: 10, bottom: 20, right: 60, top: 40, maximumLength: 5) with
        {
            Options = ["Mathematics", "", "Science", "Science"]
        };

        FormOcrPolicy.Region region = Assert.Single(
            FormOcrPolicy.MapRegions([widget], 200, 100));

        Assert.Equal(["Mathematics", "Science"], region.ChoiceValues);
        Assert.Equal(0, region.MaximumLength);
        Assert.Null(region.CharacterWhitelist);
    }

    [Fact]
    public void ClosestChoice_CorrectsNearMatchButPreservesUnrelatedText()
    {
        string[] choices = ["Mathematics", "English", "Science"];

        Assert.Equal("Science", FormOcrPolicy.ClosestChoice("Scienoe", choices));
        Assert.Equal("History", FormOcrPolicy.ClosestChoice("History", choices));
    }

    [Fact]
    public void MapRegions_IdentifiesCombCellCount()
    {
        FormOcrWidget widget = Widget("studentId", FormOcrFieldKind.Text,
            left: 10, bottom: 20, right: 90, top: 40, maximumLength: 8) with
        {
            Flags = 1L << 24
        };

        FormOcrPolicy.Region region = Assert.Single(
            FormOcrPolicy.MapRegions([widget], 200, 100));

        Assert.True(region.IsComb);
        Assert.Equal(8, region.MaximumLength);
    }

    private static FormOcrWidget Widget(string name, FormOcrFieldKind kind,
        double left, double bottom, double right, double top, int maximumLength = 0,
        int pageRotation = 0) => new(
            FieldName: name,
            FieldKind: kind,
            Flags: 0,
            MaximumLength: maximumLength,
            Options: [],
            Left: left,
            Bottom: bottom,
            Right: right,
            Top: top,
            PageBoxLeft: 0,
            PageBoxBottom: 0,
            PageBoxWidth: 100,
            PageBoxHeight: 100,
            PageRotation: pageRotation);
}
