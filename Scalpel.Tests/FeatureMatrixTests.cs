using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Exercises every WPF-free document feature against the five real bilingual sample PDFs in
    /// docs/samples/ (the GUI-only features — annotations, signing, crop, form-fill — are covered
    /// by the app's interactive/E2E harness, not here). Writes a human-readable report of every
    /// result to %TEMP%/scalpel-feature-report.md and asserts the critical invariants.
    ///
    /// The native rasterizer features (Render/Redact/Compress/OCR) are exercised separately against
    /// the same files via the app assemblies, because Docnet/PDFium is intentionally out of the unit
    /// test project; this class covers the pure-PdfSharpCore / PdfPig feature surface.
    /// </summary>
    [Collection("FontResolver")]
    public class FeatureMatrixTests
    {
        private static readonly string[] Samples =
        {
            "scalpel-sample-invoice.pdf",
            "scalpel-sample-letter.pdf",
            "scalpel-sample-report.pdf",
            "scalpel-sample-handbook.pdf",
            "scalpel-sample-contract.pdf",
        };

        private sealed record Row(string Doc, string Feature, string Status, string Details);

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scalpel.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static void EnsureFonts()
        {
            string fonts = Path.Combine(RepoRoot(), "Resources", "Fonts");
            void Reg(string fam, string file)
            {
                string p = Path.Combine(fonts, file);
                if (File.Exists(p))
                    PdfFontResolver.Instance.RegisterBundledFont(fam, File.ReadAllBytes(p), false, false);
            }
            Reg("Geist", "Geist-Regular.ttf");
            Reg("Noto Sans Hebrew", "NotoSansHebrew-Regular.ttf");
            Reg("Noto Sans", "NotoSans-Regular.ttf");
            PdfFontResolver.Install();
        }

        private static int PageCount(string path)
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
            return doc.PageCount;
        }

        [Fact]
        public void AllPureFeatures_RunAgainstEverySample_AndAreLogged()
        {
            EnsureFonts();
            string samplesDir = Path.Combine(RepoRoot(), "docs", "samples");
            string work = Path.Combine(Path.GetTempPath(), "scalpel-feature-out");
            Directory.CreateDirectory(work);
            var rows = new List<Row>();
            void Rec(string d, string f, string s, string det) => rows.Add(new Row(d, f, s, det));

            foreach (var name in Samples)
            {
                string src = Path.Combine(samplesDir, name);
                int pages = PageCount(src);

                // --- Page numbering / Bates / Header text (BatesNumberingService.StampFile) ---
                var stamps = new (string id, string label, string template, int dc, StampPosition pos)[]
                {
                    ("pagenum", "Page numbering", "{page} / {total}", 0, StampPosition.BottomRight),
                    ("bates", "Bates numbering", "BATES-{n}", 6, StampPosition.BottomLeft),
                    ("header", "Header/footer text", "CONFIDENTIAL", 0, StampPosition.TopCenter),
                };
                foreach (var (id, label, template, dc, pos) in stamps)
                {
                    try
                    {
                        string outP = Path.Combine(work, $"{id}-{name}");
                        BatesNumberingService.StampFile(src, outP, new StampOptions
                        {
                            Template = template, DigitCount = dc, StartNumber = 1,
                            Position = pos, FontSize = 10, FontFamily = "Geist",
                        });
                        int oc = PageCount(outP);
                        Rec(name, label, oc == pages ? "PASS" : "FAIL",
                            $"template '{template}' @ {pos}; pages {pages}->{oc}");
                    }
                    catch (Exception ex) { Rec(name, label, "FAIL", ex.Message); }
                }

                // --- Password protect + permission flags + remove ---
                // Common flow: one password (Protect falls back OwnerPassword=UserPassword when the
                // owner field is blank), so removing with that password has owner rights to modify.
                try
                {
                    string enc = Path.Combine(work, $"encrypted-{name}");
                    PdfEncryptionService.Protect(src, enc, new EncryptionOptions
                    {
                        UserPassword = "open123", AllowCopy = false, AllowPrint = true,
                    });
                    bool isEnc = PdfEncryptionService.IsEncrypted(enc);
                    string dec = Path.Combine(work, $"decrypted-{name}");
                    PdfEncryptionService.RemovePassword(enc, dec, "open123");
                    bool stillEnc = PdfEncryptionService.IsEncrypted(dec);
                    Rec(name, "Password protect", isEnc && !stillEnc ? "PASS" : "FAIL",
                        $"encrypted={isEnc}, after-remove={stillEnc}, copy/print permissions set");
                }
                catch (Exception ex) { Rec(name, "Password protect", "FAIL", ex.Message); }

                // --- Remove metadata ---
                try
                {
                    var before = MetadataSanitizer.ReadMetadata(src);
                    string san = Path.Combine(work, $"sanitized-{name}");
                    MetadataSanitizer.SanitizeFile(src, san);
                    var after = MetadataSanitizer.ReadMetadata(san);
                    bool cleared = string.IsNullOrEmpty(after.Title) && string.IsNullOrEmpty(after.Author)
                                && string.IsNullOrEmpty(after.Subject) && string.IsNullOrEmpty(after.Keywords);
                    Rec(name, "Remove metadata", cleared ? "PASS" : "FAIL",
                        $"title '{before.Title}' -> '{after.Title}'");
                }
                catch (Exception ex) { Rec(name, "Remove metadata", "FAIL", ex.Message); }

                // --- Full-text search (English + Hebrew) ---
                var search = new SearchService();
                // "Scalpel" appears in every doc, so it must return hits. Other terms are
                // informational (a doc may not contain them). Hebrew is informational because
                // search over Scalpel-rendered Hebrew is a known limitation (subsetted-Noto
                // ToUnicode CMap collapses) — rendering is correct, search is unreliable.
                foreach (var (lbl, term, mustHit) in new (string, string, bool)[]
                {
                    ("Search English 'Scalpel'", "Scalpel", true),
                    ("Search English 'PDF'", "PDF", false),
                    ("Search Hebrew 'עברית'", "עברית", false),
                })
                {
                    try
                    {
                        var r = search.Search(src, term);
                        string status = r.TotalHits > 0 ? "PASS" : (mustHit ? "FAIL" : "NONE");
                        Rec(name, lbl, status, $"{r.TotalHits} hit(s) on pages [{string.Join(",", r.ResultPages)}]");
                    }
                    catch (Exception ex) { Rec(name, lbl, "FAIL", ex.Message); }
                }
            }

            // --- Merge (handbook 3 + contract 5 -> 8) ---
            try
            {
                string a = Path.Combine(samplesDir, "scalpel-sample-handbook.pdf");
                string b = Path.Combine(samplesDir, "scalpel-sample-contract.pdf");
                using var merged = new PdfDocument();
                foreach (var f in new[] { a, b })
                {
                    using var imp = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < imp.PageCount; i++) merged.AddPage(imp.Pages[i]);
                }
                string outP = Path.Combine(work, "merged-handbook+contract.pdf");
                merged.Save(outP);
                Rec("handbook+contract", "Merge", merged.PageCount == 8 ? "PASS" : "FAIL",
                    $"3+5 -> {merged.PageCount} pages");
            }
            catch (Exception ex) { Rec("handbook+contract", "Merge", "FAIL", ex.Message); }

            // --- Split / extract pages (first 2 pages of the 5-page contract) ---
            try
            {
                string src = Path.Combine(samplesDir, "scalpel-sample-contract.pdf");
                using var imp = PdfReader.Open(src, PdfDocumentOpenMode.Import);
                using var split = new PdfDocument();
                for (int i = 0; i < 2; i++) split.AddPage(imp.Pages[i]);
                string outP = Path.Combine(work, "split-contract-p1-2.pdf");
                split.Save(outP);
                Rec("contract", "Split / extract pages", split.PageCount == 2 ? "PASS" : "FAIL",
                    $"extracted 2 of 5 pages");
            }
            catch (Exception ex) { Rec("contract", "Split / extract pages", "FAIL", ex.Message); }

            // ---- write the markdown report ----
            var sb = new StringBuilder();
            sb.AppendLine("# Scalpel feature test — 5 bilingual sample PDFs");
            sb.AppendLine();
            sb.AppendLine("Pure-PdfSharpCore / PdfPig features (this xUnit test). Native rasterizer");
            sb.AppendLine("features (Render/Redact/Compress/OCR) are validated separately.");
            sb.AppendLine();
            sb.AppendLine("| Document | Feature | Status | Details |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in rows)
                sb.AppendLine($"| {r.Doc} | {r.Feature} | {r.Status} | {r.Details} |");
            sb.AppendLine();
            var byStatus = rows.GroupBy(r => r.Status).OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");
            sb.AppendLine($"**Totals:** {string.Join(", ", byStatus)} (of {rows.Count} checks)");
            string report = Path.Combine(Path.GetTempPath(), "scalpel-feature-report.md");
            File.WriteAllText(report, sb.ToString());

            // ---- assert the critical invariants (no hard FAILs) ----
            var fails = rows.Where(r => r.Status == "FAIL").ToList();
            Assert.True(fails.Count == 0,
                "Feature failures:\n" + string.Join("\n", fails.Select(f => $"  {f.Doc}/{f.Feature}: {f.Details}")));
        }
    }
}
