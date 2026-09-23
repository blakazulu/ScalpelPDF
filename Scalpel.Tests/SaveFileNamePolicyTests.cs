using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests;

public sealed class SaveFileNamePolicyTests
{
    [Theory]
    [InlineData("exam", "exam.pdf")]
    [InlineData("exam.final", "exam.final.pdf")]
    [InlineData("exam.final.PDF", "exam.final.PDF")]
    public void RequiredPdfExtensionHandlesDotsInsideFileName(string typed, string expected)
    {
        Assert.Equal(expected, SaveFileNamePolicy.ApplyExtension(typed, ".pdf", addExtension: true, requireExtension: true));
    }

    [Fact]
    public void OrdinaryDialogPreservesDeliberatelyTypedExtension()
    {
        Assert.Equal("notes.txt", SaveFileNamePolicy.ApplyExtension(
            "notes.txt", ".pdf", addExtension: true, requireExtension: false));
    }

    [Fact]
    public void DisabledExtensionHandlingLeavesNameUntouched()
    {
        Assert.Equal("exam", SaveFileNamePolicy.ApplyExtension(
            "exam", ".pdf", addExtension: false, requireExtension: true));
    }
}
