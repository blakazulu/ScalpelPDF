using System;
using System.IO;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Docnet hands PDFium a managed FPDF_FORMFILLINFO that is pinned only for the init call, but
    /// PDFium keeps the pointer and reads it again in FPDFDOC_ExitFormFillEnvironment. A GC in
    /// between moves the block and teardown reads a stale address - an uncatchable
    /// AccessViolationException (the E2E crash in RefreshPageList's thumbnail loader). Provoking
    /// the AV would kill the test host, so these tests hold the invariant instead: the block PDFium
    /// was given is pinned for the reader's whole life, including after an annotation render (where
    /// Docnet would otherwise create its own unpinned one).
    /// </summary>
    [Collection("Pdfium")]
    public class PinnedDocReaderTests
    {
        private static string BuildPdf(int pages)
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-pin-{Path.GetRandomFileName()}.pdf");
            using var doc = new PdfDocument();
            for (int i = 0; i < pages; i++)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(200);
                page.Height = XUnit.FromPoint(200);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(XBrushes.LightGray, new XRect(10, 10, 180, 180));
            }
            doc.Save(path);
            return path;
        }

        [Fact]
        public void TheFormFillBlockStaysPinnedAcrossRendersAndCollections()
        {
            string path = BuildPdf(6);
            try
            {
                var reader = PdfiumGate.Run(() => PinnedDocReader.Open(path, new PageDimensions(128, 256)));
                try
                {
                    Assert.True(PdfiumGate.Run(() => PinnedDocReader.IsFormFillPinned(reader)),
                        "the reader was opened without a pinned form-fill block");
                    // The thumbnail loader's shape: one reader, a page at a time, allocation and
                    // collections in between.
                    for (int i = 0; i < 6; i++)
                    {
                        int page = i;
                        var raw = PdfiumGate.Run(() =>
                        {
                            using var pr = reader.GetPageReader(page);
                            return pr.GetImage(new NaiveTransparencyRemover(), AnnotationRenderPolicy.ForOutput());
                        });
                        Assert.NotNull(raw);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        // Docnet adopted our block rather than building its own unpinned one.
                        Assert.True(PdfiumGate.Run(() => PinnedDocReader.IsFormFillPinned(reader)),
                            $"after rendering page {page} the form-fill block PDFium holds is not the pinned one");
                    }
                }
                finally { PdfiumGate.Run(() => reader.Dispose()); }
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void RepeatedOpenRenderDisposeWithCollectionsIsSafe()
        {
            string path = BuildPdf(3);
            try
            {
                for (int round = 0; round < 20; round++)
                {
                    var reader = PdfiumGate.Run(() => PinnedDocReader.Open(path, new PageDimensions(200, 200)));
                    try
                    {
                        for (int p = 0; p < 3; p++)
                        {
                            int page = p;
                            PdfiumGate.Run(() =>
                            {
                                using var pr = reader.GetPageReader(page);
                                _ = pr.GetImage(new NaiveTransparencyRemover(), AnnotationRenderPolicy.ForOutput());
                            });
                            _ = new byte[64 * 1024];
                            GC.Collect();
                        }
                        Assert.Equal(3, PdfiumGate.Run(() => reader.GetPageCount()));
                    }
                    finally { PdfiumGate.Run(() => reader.Dispose()); }
                }
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
