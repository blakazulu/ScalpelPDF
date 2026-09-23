using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// A damaged-file safety gate, modelled on the equivalent upstream KillerPDF publishes with its
    /// public corpus ("0 crashes or timeouts" over deliberately hostile input).
    ///
    /// <para>Scalpel's existing FeatureMatrixTests run the feature surface over five WELL-FORMED
    /// sample documents. This covers the other half: files that are truncated, lying about their
    /// own structure, self-referential, or absurdly sized. PDFs are untrusted input, and Scalpel's
    /// house rule is that its I/O and parsing swallow and fall back rather than surfacing an
    /// exception mid-edit - so the invariant here is that no hostile file can hang the pipeline or
    /// take the process down. A file being REFUSED is a perfectly good outcome; hanging is not.</para>
    ///
    /// <para>A scoreboard is written to %TEMP%/scalpel-hostile-report.md, mirroring the way
    /// upstream publishes its own pass/skip/fail counts.</para>
    /// </summary>
    // PdfReopen writes recovery warnings to the process-wide Logger, so this shares the
    // "Logger" collection to stay off the toes of the tests that assert on that log file.
    [Collection("Logger")]
    public class HostileCorpusTests
    {
        /// <summary>Per-file budget. Real work here is milliseconds; this only catches a hang.</summary>
        private static readonly TimeSpan PerFileBudget = TimeSpan.FromSeconds(20);

        private sealed record Outcome(string Name, string Stage, string Result, long Ms);

        // ---- hostile corpus -------------------------------------------------

        private static Dictionary<string, byte[]> BuildCorpus()
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            byte[] good = GoodPdf();

            files["empty"] = [];
            files["header-only"] = Latin1("%PDF-1.7\n");
            files["not-a-pdf"] = Latin1(new string('A', 4096));
            files["header-then-garbage"] = Latin1("%PDF-1.7\n" + new string('\x01', 8192));
            files["truncated-half"] = good.Take(good.Length / 2).ToArray();
            files["truncated-to-header"] = good.Take(Math.Min(32, good.Length)).ToArray();
            files["no-eof-marker"] = good.Take(good.Length - 6).ToArray();

            // Structure that lies about itself.
            files["startxref-past-eof"] = Rewrite(good, s =>
                ReplaceLast(s, "startxref", "startxref\n999999999\n%%EOF\n%"));
            files["startxref-garbage"] = Rewrite(good, s =>
            {
                int i = s.LastIndexOf("startxref", StringComparison.Ordinal);
                return i < 0 ? s : s[..i] + "startxref\nNOTANUMBER\n%%EOF\n";
            });
            files["xref-keyword-corrupted"] = Rewrite(good, s => s.Replace("\nxref\n", "\nxrEf\n"));
            files["trailer-size-absurd"] = Rewrite(good, s => s.Replace("/Size 5", "/Size 2147483647"));
            files["negative-stream-length"] = Rewrite(good, s => s.Replace("/Length ", "/Length -"));

            // Object graphs that do not terminate or make sense.
            files["cyclic-page-parent"] = CyclicParent();
            files["zero-page-tree"] = ZeroPages();
            files["page-count-lies"] = Rewrite(ZeroPages(), s => s.Replace("/Count 0", "/Count 9999"));
            files["degenerate-mediabox"] = PageWithBox("[0 0 0 0]");
            files["absurd-mediabox"] = PageWithBox("[0 0 99999999 99999999]");
            files["negative-mediabox"] = PageWithBox("[0 0 -600 -800]");
            files["non-numeric-mediabox"] = PageWithBox("[0 0 (six hundred) 800]");
            files["dangling-root"] = Rewrite(good, s => s.Replace("/Root 1 0 R", "/Root 9999 0 R"));
            files["dangling-outlines"] = Rewrite(good, s =>
                s.Replace("/Type/Catalog", "/Type/Catalog/Outlines 4242 0 R"));
            files["encrypt-without-dict"] = Rewrite(good, s =>
                s.Replace("/Root 1 0 R", "/Root 1 0 R/Encrypt 8888 0 R"));
            files["deeply-nested-arrays"] = Rewrite(good, s =>
                s.Replace("/MediaBox[0 0 200 200]",
                          "/MediaBox[0 0 200 200]/Junk" + new string('[', 500) + new string(']', 500)));

            return files;
        }

        private static byte[] GoodPdf()
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = PdfSharpCore.Drawing.XUnit.FromPoint(200);
            page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(200);
            using var ms = new MemoryStream();
            doc.Save(ms, false);
            return ms.ToArray();
        }

        private static byte[] Latin1(string s) => Encoding.GetEncoding(28591).GetBytes(s);

        private static byte[] Rewrite(byte[] src, Func<string, string> edit)
            => Latin1(edit(Encoding.GetEncoding(28591).GetString(src)));

        private static string ReplaceLast(string s, string find, string repl)
        {
            int i = s.LastIndexOf(find, StringComparison.Ordinal);
            return i < 0 ? s : s[..i] + repl + s[(i + find.Length)..];
        }

        private static byte[] Handmade(string body) => Latin1(
            "%PDF-1.7\n" + body + "\ntrailer\n<</Size 4/Root 1 0 R>>\nstartxref\n0\n%%EOF\n");

        private static byte[] CyclicParent() => Handmade(
            "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n" +
            "2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1/Parent 3 0 R>> endobj\n" +
            "3 0 obj <</Type/Page/Parent 2 0 R/Kids[2 0 R]/MediaBox[0 0 200 200]>> endobj\n");

        private static byte[] ZeroPages() => Handmade(
            "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n" +
            "2 0 obj <</Type/Pages/Kids[]/Count 0>> endobj\n");

        private static byte[] PageWithBox(string box) => Handmade(
            "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n" +
            "2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1>> endobj\n" +
            "3 0 obj <</Type/Page/Parent 2 0 R/MediaBox" + box + ">> endobj\n");

        // ---- the gate -------------------------------------------------------

        /// <summary>
        /// Runs one guarded operation under a time budget. Any exception is an acceptable outcome
        /// (Scalpel catches these at the call site); exceeding the budget is not.
        /// </summary>
        private static Outcome Run(string name, string stage, Action work)
        {
            var sw = Stopwatch.StartNew();
            string result;
            var task = Task.Run(() =>
            {
                try { work(); return "ok"; }
                catch (Exception ex) { return "refused:" + ex.GetType().Name; }
            });

            result = task.Wait(PerFileBudget) ? task.Result : "TIMEOUT";
            sw.Stop();
            return new Outcome(name, stage, result, sw.ElapsedMilliseconds);
        }

        [Fact]
        public void NoHostileFileHangsOrCrashesThePipeline()
        {
            var corpus = BuildCorpus();
            var outcomes = new List<Outcome>();
            string dir = Path.Combine(Path.GetTempPath(), "scalpel-hostile-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);

            try
            {
                foreach (var entry in corpus)
                {
                    // net48's KeyValuePair has no Deconstruct.
                    string name = entry.Key;
                    string path = Path.Combine(dir, name + ".pdf");
                    File.WriteAllBytes(path, entry.Value);

                    // 1. The open path, including Scalpel's own recovery helper.
                    outcomes.Add(Run(name, "open", () =>
                    {
                        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
                        _ = doc.PageCount;
                    }));

                    outcomes.Add(Run(name, "reopen-recover", () =>
                    {
                        using var doc = PdfReopen.OpenModify(path, (_, _) => false,
                            () => Path.Combine(dir, Guid.NewGuid().ToString("N")[..8] + ".pdf"), out _);
                        _ = doc.PageCount;
                    }));

                    // 2. The save guard, which walks the object graph looking for defects to repair
                    //    and is therefore the piece most exposed to a malicious graph.
                    outcomes.Add(Run(name, "save-guard", () =>
                    {
                        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
                        PdfSaveGuard.PrepareForSave(doc);
                        _ = PdfSaveGuard.FindPagesOutsideRange(doc);
                        PdfSaveGuard.Save(doc, Path.Combine(dir, name + "-saved.pdf"));
                    }));

                    // 3. Text extraction (PdfPig), which parses independently of PdfSharpCore.
                    outcomes.Add(Run(name, "search", () => new SearchService().Search(path, "test")));

                    // 4. Metadata scrubbing over the same hostile graph.
                    outcomes.Add(Run(name, "sanitize", () =>
                        MetadataSanitizer.SanitizeFile(path, Path.Combine(dir, name + "-clean.pdf"))));
                }

                WriteReport(corpus.Count, outcomes);

                var timeouts = outcomes.Where(o => o.Result == "TIMEOUT").ToList();
                Assert.True(timeouts.Count == 0,
                    "hostile input hung the pipeline: " +
                    string.Join(", ", timeouts.Select(t => $"{t.Name}/{t.Stage}")));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void TheGoodControlFileStillOpensAndSaves()
        {
            // Guards the gate itself: if the corpus builder broke, everything would "pass" by
            // being refused, so prove a healthy document still round-trips.
            string dir = Path.Combine(Path.GetTempPath(), "scalpel-hostile-ctl-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "good.pdf");
                File.WriteAllBytes(path, GoodPdf());

                using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
                {
                    Assert.Equal(1, doc.PageCount);
                    PdfSaveGuard.Save(doc, Path.Combine(dir, "good-out.pdf"));
                }
                using var reopened = PdfReader.Open(Path.Combine(dir, "good-out.pdf"), PdfDocumentOpenMode.Modify);
                Assert.Equal(1, reopened.PageCount);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static void WriteReport(int fileCount, List<Outcome> outcomes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Scalpel damaged-file safety report").AppendLine();
            sb.AppendLine($"**Hostile files:** {fileCount} &nbsp; **Operations:** {outcomes.Count} " +
                          $"&nbsp; **Timeouts:** {outcomes.Count(o => o.Result == "TIMEOUT")}");
            sb.AppendLine();
            sb.AppendLine("| Stage | Completed | Refused | Timeout | Slowest |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var stage in outcomes.Select(o => o.Stage).Distinct())
            {
                var rows = outcomes.Where(o => o.Stage == stage).ToList();
                sb.AppendLine($"| {stage} | {rows.Count(r => r.Result == "ok")} " +
                              $"| {rows.Count(r => r.Result.StartsWith("refused", StringComparison.Ordinal))} " +
                              $"| {rows.Count(r => r.Result == "TIMEOUT")} | {rows.Max(r => r.Ms)} ms |");
            }
            sb.AppendLine().AppendLine("A refusal is a correct outcome: the file is not usable and Scalpel");
            sb.AppendLine("declines it. Only a timeout is a defect.").AppendLine();
            sb.AppendLine("| File | Stage | Result | ms |");
            sb.AppendLine("|---|---|---|---:|");
            foreach (var o in outcomes)
                sb.AppendLine($"| {o.Name} | {o.Stage} | {o.Result} | {o.Ms} |");

            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "scalpel-hostile-report.md"), sb.ToString());
            }
            catch { }
        }
    }
}
