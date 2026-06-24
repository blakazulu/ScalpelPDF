using System.IO;
using System.Text;
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
            WriteHebrew1P(outDir),
            WriteMissingFont1P(outDir),
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

    /// <summary>
    /// A 1-page PDF with the Hebrew word "shalom" (\u05E9\u05DC\u05D5\u05DD)
    /// drawn near the page center using Arial (which has Hebrew glyphs).
    /// Used by Scenario B (edit existing Hebrew text).
    /// Positioned at ~45% of page width/height to match ClickCanvas()/DoubleClickCanvas()
    /// which click at the 45% fraction of the PageImage element.
    /// </summary>
    private static CorpusFile WriteHebrew1P(string dir)
    {
        string path = System.IO.Path.Combine(dir, "hebrew-1p.pdf");
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Arial is installed on all Windows machines and has Hebrew glyphs.
            var font = new XFont("Arial", 24);
            // shalom: \u05E9\u05DC\u05D5\u05DD
            string shalom = "\u05E9\u05DC\u05D5\u05DD";
            double x = page.Width  * 0.45;
            double y = page.Height * 0.45;
            gfx.DrawString(shalom, font, XBrushes.Black, x, y);
        }
        doc.Save(path);
        return new CorpusFile("hebrew-1p", path, 1);
    }

    /// <summary>
    /// A 1-page PDF whose text references a font name that is NOT installed on the
    /// system ("MadeUpFontXYZ123"), so Scalpel's FontResolver reports it as missing
    /// and shows the font-missing toast when the user tries to edit the text.
    ///
    /// Strategy: use PdfSharpCore to produce a valid, PdfPig-readable PDF (with a
    /// ToUnicode CMap so word extraction works), then binary-patch the saved file to
    /// replace the real /BaseFont name with "/MadeUpFontXYZ123" (padded to the same
    /// byte length so the xref offsets remain valid).
    ///
    /// This ensures:
    ///   - pdfium opens and renders it (it substitutes a glyph)
    ///   - PdfPig extracts words (ToUnicode CMap from PdfSharpCore is intact)
    ///   - FontResolver.Resolve("MadeUpFontXYZ123",...).IsInstalled == false -> toast
    /// </summary>
    private static CorpusFile WriteMissingFont1P(string dir)
    {
        string path = System.IO.Path.Combine(dir, "missingfont-1p.pdf");

        // Step 1: Write a valid PDF with Arial text centered near the page.
        // PdfSharpCore embeds font + ToUnicode CMap so PdfPig can extract words.
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 24);
            // "Hello World" placed near center (same fraction as ClickCanvas/DoubleClickCanvas).
            gfx.DrawString("Hello World", font, XBrushes.Black,
                new XRect(0, 0, page.Width, page.Height), XStringFormats.Center);
            doc.Save(path);
        }

        // Step 2: Binary-patch the /BaseFont name in the saved bytes.
        // PdfSharpCore emits the font name with a subset prefix: /XXXXXX+Arial
        // (6-char uppercase prefix + '+' + family name).
        // FontResolver.Resolve strips the prefix, so replacing "Arial" (after '+')
        // with "FkFnt" (same length: 5 chars) makes FontResolver look up "FkFnt" —
        // which is not installed — and show the font-missing toast.
        // Replacing "Arial" with "FkFnt" everywhere keeps all byte offsets valid
        // (same length) so xref offsets are not disturbed.
        byte[] pdfBytes = File.ReadAllBytes(path);

        byte[] arialAscii = Encoding.ASCII.GetBytes("Arial");  // 5 bytes
        byte[] fkFntAscii = Encoding.ASCII.GetBytes("FkFnt");  // 5 bytes — MUST be same length

        for (int i = 0; i <= pdfBytes.Length - arialAscii.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < arialAscii.Length; j++)
            {
                if (pdfBytes[i + j] != arialAscii[j]) { match = false; break; }
            }
            if (match)
            {
                // Only patch if the character before is '+' or '/' (font name separator),
                // not if 'Arial' appears inside some other unrelated string.
                if (i > 0 && (pdfBytes[i - 1] == '+' || pdfBytes[i - 1] == '/'))
                {
                    for (int j = 0; j < fkFntAscii.Length; j++)
                        pdfBytes[i + j] = fkFntAscii[j];
                }
            }
        }

        File.WriteAllBytes(path, pdfBytes);

        // Verify with PdfPig: must open and have >= 1 page + extractable words.
        try
        {
            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(path);
            if (pigDoc.NumberOfPages < 1)
                throw new InvalidOperationException("PdfPig found 0 pages in missingfont-1p");
            var words = pigDoc.GetPage(1).GetWords().ToList();
            Console.WriteLine($"[Corpus] missingfont-1p: {words.Count} words extracted by PdfPig. " +
                              "Font name in first letter: " +
                              (pigDoc.GetPage(1).Letters.Count > 0
                                   ? (pigDoc.GetPage(1).Letters[0].FontName ?? "(null)")
                                   : "(no letters)"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Corpus] missingfont-1p PdfPig check failed: {ex.Message}");
        }

        return new CorpusFile("missingfont-1p", path, 1);
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
