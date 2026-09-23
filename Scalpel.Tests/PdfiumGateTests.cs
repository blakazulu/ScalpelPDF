using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// PDFium is single-threaded and Docnet stands up a form-fill environment per document,
    /// tearing it down on Dispose. Two documents open at once corrupt that global state and the
    /// process dies with an AccessViolationException inside FPDFDOC_ExitFormFillEnvironment -
    /// which .NET Framework will not let the app catch, so it is a silent kill.
    ///
    /// <para>Scalpel really does this: sidebar thumbnails ran two at a time while background tasks
    /// streamed continuous and grid tiles and the UI thread rendered the primary page. This test
    /// reproduces the shape of that concurrency through PdfiumGate and proves it survives.</para>
    /// </summary>
    // PDFium is a process-wide singleton, so every test class that drives it directly must
    // run serially - exactly the discipline PdfiumGate enforces in the app.
    [Collection("Pdfium")]
    public class PdfiumGateTests
    {
        private static string BuildPdf(string path, int pages)
        {
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
        public void ConcurrentRenderingThroughTheGateIsSafe()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-gate-{Path.GetRandomFileName()}.pdf");
            BuildPdf(path, 4);
            var failures = new ConcurrentBag<string>();

            try
            {
                // Eight workers doing exactly what the app does: open a document, render pages,
                // dispose. Without the gate this reliably dies in the native teardown.
                Parallel.For(0, 8, w =>
                {
                    try
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            PdfiumGate.Run(() =>
                            {
                                using var dr = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(200, 200));
                                for (int p = 0; p < dr.GetPageCount(); p++)
                                {
                                    using var pr = dr.GetPageReader(p);
                                    _ = pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                                    AnnotationRenderPolicy.ForOutput());
                                    _ = pr.GetPageWidth();
                                }
                            });
                        }
                    }
                    catch (Exception ex) { failures.Add($"worker {w}: {ex.GetType().Name}: {ex.Message}"); }
                });

                Assert.True(failures.IsEmpty, string.Join(" | ", failures));
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void RepeatedGatedRenderingOnOneThreadIsSafe()
        {
            // What the gate guarantees today: serialized, same-thread PDFium use is stable. This
            // is the shape every production path now has.
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-gate-seq-{Path.GetRandomFileName()}.pdf");
            BuildPdf(path, 3);
            try
            {
                for (int i = 0; i < 25; i++)
                {
                    PdfiumGate.Run(() =>
                    {
                        using var dr = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(200, 200));
                        for (int p = 0; p < dr.GetPageCount(); p++)
                        {
                            using var pr = dr.GetPageReader(p);
                            _ = pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                            AnnotationRenderPolicy.ForOutput());
                        }
                    });
                }
            }
            finally { try { File.Delete(path); } catch { } }
        }

        [Fact]
        public void TheGateIsReentrantOnOneThread()
        {
            // DocnetPageRasterizer holds the gate for its lifetime, and code inside that scope
            // may render again. A non-reentrant gate would deadlock.
            bool inner = false;
            PdfiumGate.Run(() => PdfiumGate.Run(() => { inner = true; }));
            Assert.True(inner);
        }

        [Fact]
        public void TheWorkerStaysAvailableAfterAJobCompletes()
        {
            // A completed job must leave the PDFium thread free for the next caller.
            PdfiumGate.Run(() => { });
            bool ran = false;
            var t = Task.Run(() => PdfiumGate.Run(() => { ran = true; }));
            Assert.True(t.Wait(TimeSpan.FromSeconds(10)), "the PDFium thread did not take more work");
            Assert.True(ran);
        }

        [Fact]
        public void TheRasterizerSerializesAcrossThreads()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-gate-r-{Path.GetRandomFileName()}.pdf");
            BuildPdf(path, 2);
            var failures = new ConcurrentBag<string>();
            try
            {
                Parallel.For(0, 6, w =>
                {
                    try
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            using var rast = new DocnetPageRasterizer(path, 300);
                            _ = rast.RenderPage(0);
                            _ = rast.PageSizePt(0);
                        }
                    }
                    catch (Exception ex) { failures.Add($"worker {w}: {ex.GetType().Name}: {ex.Message}"); }
                });
                Assert.True(failures.IsEmpty, string.Join(" | ", failures));
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
