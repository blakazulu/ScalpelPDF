using System.IO;
using System.Linq;
using Scalpel.E2E;
using UglyToad.PdfPig;
using Xunit;

namespace Scalpel.Tests.E2E;

public class CorpusTests
{
    [Fact]
    public void Generate_ProducesLoadBearingFixturesWithCorrectPageCounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");
        var files = Corpus.Generate(dir);

        var simple = files.Single(f => f.Key == "simple-1p");
        var large = files.Single(f => f.Key == "large-50p");

        Assert.True(File.Exists(simple.Path));
        using (var doc = PdfDocument.Open(simple.Path))
            Assert.Equal(1, doc.NumberOfPages);
        using (var doc = PdfDocument.Open(large.Path))
            Assert.Equal(50, doc.NumberOfPages);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Generate_CorruptedFile_HasPdfHeaderButIsNotAValidDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"corpus-{Guid.NewGuid():N}");
        var files = Corpus.Generate(dir);
        var corrupted = files.Single(f => f.Key == "corrupted");

        var bytes = File.ReadAllBytes(corrupted.Path);
        Assert.True(bytes.Length > 5);
        Assert.Equal((byte)'%', bytes[0]);                 // starts with %PDF-
        Assert.Throws<UglyToad.PdfPig.Core.PdfDocumentFormatException>(
            () => PdfDocument.Open(corrupted.Path));

        Directory.Delete(dir, true);
    }
}
