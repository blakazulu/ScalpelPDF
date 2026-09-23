using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using SixLabors.ImageSharp;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Preflight answers "will this document survive leaving my machine": pages a reader will
    /// refuse, images that print blurry, fonts the recipient does not have.
    /// </summary>
    public class PreflightServiceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "scalpel-preflight-" + Guid.NewGuid().ToString("N")[..8]);

        public PreflightServiceTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
            GC.SuppressFinalize(this);
        }

        private string Path_(string name) => System.IO.Path.Combine(_dir, name);

        private static byte[] SolidPng(int w, int h)
        {
            using var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(w, h);
            img.ProcessPixelRows(a =>
            {
                for (int y = 0; y < a.Height; y++)
                {
                    var row = a.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                        row[x] = new SixLabors.ImageSharp.PixelFormats.Rgba32(90, 120, 220, 255);
                }
            });
            using var ms = new MemoryStream();
            ImageExtensions.SaveAsPng(img, ms);
            return ms.ToArray();
        }

        private string MakePdf(string name, Action<PdfDocument> build)
        {
            string p = Path_(name);
            using var doc = new PdfDocument();
            build(doc);
            doc.Save(p);
            return p;
        }

        [Fact]
        public void AHealthyDocumentReportsNoProblems()
        {
            string p = MakePdf("clean.pdf", d =>
            {
                var page = d.AddPage();
                page.Width = XUnit.FromPoint(595);
                page.Height = XUnit.FromPoint(842);
            });

            var report = PreflightService.Inspect(p);
            Assert.Equal(1, report.PageCount);
            Assert.Equal(0, report.ProblemCount);
        }

        [Fact]
        public void FlagsPagesOutsideTheAcceptedSizeRange()
        {
            string p = MakePdf("huge.pdf", d =>
            {
                var page = d.AddPage();
                page.Width = XUnit.FromPoint(20000);   // beyond Adobe's 14400 pt limit
                page.Height = XUnit.FromPoint(800);
            });

            var report = PreflightService.Inspect(p);
            var finding = Assert.Single(report.Findings,
                f => f.Category == "Page size" && f.Severity == PreflightSeverity.Problem);
            Assert.Equal([1], finding.Pages);
            Assert.True(report.ProblemCount >= 1);
        }

        [Fact]
        public void FlagsALowResolutionImage()
        {
            // A 40x40 pixel image stretched over a 400x400 point box is 7.2 DPI.
            string p = MakePdf("lowdpi.pdf", d =>
            {
                var page = d.AddPage();
                page.Width = XUnit.FromPoint(500);
                page.Height = XUnit.FromPoint(500);
                byte[] png = SolidPng(40, 40);
                using var gfx = XGraphics.FromPdfPage(page);
                var xi = XImage.FromStream(() => new MemoryStream(png));
                gfx.DrawImage(xi, 20, 20, 400, 400);
            });

            var report = PreflightService.Inspect(p);
            var img = report.Findings.Where(f => f.Category == "Images").ToList();
            Assert.NotEmpty(img);
            Assert.Contains(img, f => f.Severity == PreflightSeverity.Problem);
        }

        [Fact]
        public void DoesNotFlagAHighResolutionImage()
        {
            // 1200x1200 pixels over a 200x200 point box is 432 DPI.
            string p = MakePdf("highdpi.pdf", d =>
            {
                var page = d.AddPage();
                page.Width = XUnit.FromPoint(300);
                page.Height = XUnit.FromPoint(300);
                byte[] png = SolidPng(1200, 1200);
                using var gfx = XGraphics.FromPdfPage(page);
                var xi = XImage.FromStream(() => new MemoryStream(png));
                gfx.DrawImage(xi, 20, 20, 200, 200);
            });

            var report = PreflightService.Inspect(p);
            Assert.DoesNotContain(report.Findings,
                f => f.Category == "Images" && f.Severity != PreflightSeverity.Info);
        }

        [Fact]
        public void ReportsMixedPageSizes()
        {
            string p = MakePdf("mixed.pdf", d =>
            {
                var a = d.AddPage(); a.Width = XUnit.FromPoint(595); a.Height = XUnit.FromPoint(842);
                var b = d.AddPage(); b.Width = XUnit.FromPoint(612); b.Height = XUnit.FromPoint(792);
            });

            var report = PreflightService.Inspect(p);
            Assert.Contains(report.Findings,
                f => f.Category == "Page size" && f.Message.Contains("different page sizes"));
        }

        [Fact]
        public void ReportsFormFields()
        {
            string p = Path_("form.pdf");
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                var field = new PdfDictionary(doc);
                field.Elements["/FT"] = new PdfName("/Tx");
                field.Elements["/T"] = new PdfString("Name");
                var fields = new PdfArray(doc);
                fields.Elements.Add(field);
                var acro = new PdfDictionary(doc);
                acro.Elements["/Fields"] = fields;
                doc.Internals.Catalog.Elements["/AcroForm"] = acro;
                doc.Save(p);
            }

            var report = PreflightService.Inspect(p);
            Assert.Contains(report.Findings, f => f.Category == "Forms");
        }

        [Fact]
        public void ReportsCertificationPermissionsAsAWarning()
        {
            string p = Path_("perms.pdf");
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Internals.Catalog.Elements["/Perms"] = new PdfDictionary(doc);
                doc.Save(p);
            }

            var report = PreflightService.Inspect(p);
            Assert.Contains(report.Findings,
                f => f.Category == "Signatures" && f.Severity == PreflightSeverity.Warning);
        }

        [Fact]
        public void AnUnreadableFileIsReportedNotThrown()
        {
            string p = Path_("garbage.pdf");
            File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4, 5 });

            var report = PreflightService.Inspect(p);
            Assert.Equal(0, report.PageCount);
            Assert.Contains(report.Findings,
                f => f.Category == "Structure" && f.Severity == PreflightSeverity.Problem);
        }

        [Fact]
        public void AMissingFileIsReportedNotThrown()
        {
            var report = PreflightService.Inspect(Path_("does-not-exist.pdf"));
            Assert.Contains(report.Findings, f => f.Severity == PreflightSeverity.Problem);
        }

        [Fact]
        public void CleanReportKnowsItIsClean()
        {
            string p = MakePdf("clean2.pdf", d =>
            {
                var page = d.AddPage();
                page.Width = XUnit.FromPoint(595);
                page.Height = XUnit.FromPoint(842);
                // A fresh page has no explicit /MediaBox array yet, so build the trim box.
                var trim = new PdfArray(d);
                foreach (var v in new[] { 0, 0, 595, 842 }) trim.Elements.Add(new PdfReal(v));
                page.Elements["/TrimBox"] = trim;
            });

            var report = PreflightService.Inspect(p);
            Assert.Equal(0, report.ProblemCount);
        }
    }
}
