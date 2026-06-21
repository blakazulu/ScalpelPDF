using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace Scalpel.E2E;

public sealed record CorpusFile(string Key, string Path, int ExpectedPages);

public static class Corpus
{
    public static IReadOnlyList<CorpusFile> Generate(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var files = new List<CorpusFile>
        {
            WriteSimple(outDir),
            WriteLarge(outDir),
            WriteImageOnly(outDir),
            WriteCorrupted(outDir),
        };
        return files;
    }

    private static CorpusFile WriteSimple(string dir)
    {
        string path = System.IO.Path.Combine(dir, "simple-1p.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 20);
            gfx.DrawString("Scalpel E2E — simple 1 page", font, XBrushes.Black,
                new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
        }
        doc.Save(path);
        return new CorpusFile("simple-1p", path, 1);
    }

    private static CorpusFile WriteLarge(string dir)
    {
        string path = System.IO.Path.Combine(dir, "large-50p.pdf");
        using var doc = new PdfDocument();
        var font = new XFont("Arial", 20);
        for (int i = 1; i <= 50; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {i} of 50", font, XBrushes.Black,
                new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
        }
        doc.Save(path);
        return new CorpusFile("large-50p", path, 50);
    }

    private static CorpusFile WriteImageOnly(string dir)
    {
        string path = System.IO.Path.Combine(dir, "image-only.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // A drawn filled rectangle stands in for an image — no text content.
            gfx.DrawRectangle(XBrushes.SteelBlue, new XRect(40, 40, 200, 200));
            gfx.DrawEllipse(XBrushes.Goldenrod, new XRect(80, 80, 120, 120));
        }
        doc.Save(path);
        return new CorpusFile("image-only", path, 1);
    }

    private static CorpusFile WriteCorrupted(string dir)
    {
        string path = System.IO.Path.Combine(dir, "corrupted.pdf");
        // Valid header, then truncated garbage so the parser must fail/repair.
        byte[] header = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n");
        byte[] garbage = [0x25, 0x25, 0x00, 0xFF, 0xFE, 0x0A, 0x42, 0x41, 0x44];
        File.WriteAllBytes(path, [.. header, .. garbage]);
        return new CorpusFile("corrupted", path, 0);
    }

    public static IReadOnlyList<string> RealFixtures(string fixturesDir)
    {
        try
        {
            if (!Directory.Exists(fixturesDir)) return [];
            return [.. Directory.GetFiles(fixturesDir, "*.pdf")];
        }
        catch { return []; }
    }
}
