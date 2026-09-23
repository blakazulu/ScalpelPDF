using System;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Scalpel.Tests
{
    /// <summary>
    /// Scalpel currently rasterizes every page with a bare GetImage, so annotations a PDF already
    /// carries (highlights, stamps, ink, notes added by another editor) are invisible in the
    /// viewer AND are silently dropped from print, flatten and export. Docnet exposes
    /// RenderFlags.RenderAnnotations for exactly this.
    ///
    /// <para>Upstream KillerPDF reported (#141) that enabling Docnet's annotation flag corrupted
    /// PDFium state and crashed on a later native call, because it stood up a form-fill
    /// environment while the page was still in use. That claim has to be tested before Scalpel
    /// relies on the flag, so these tests both prove the flag works and hammer it against the
    /// crash they described.</para>
    /// </summary>
    // PDFium is a process-wide singleton, so every test class that drives it directly must
    // run serially - exactly the discipline PdfiumGate enforces in the app.
    [Collection("Pdfium")]
    public class AnnotationRenderTests
    {
        /// <summary>A page with a big opaque red square annotation carrying its own appearance
        /// stream, plus an AcroForm text field widget (the combination upstream said was unsafe).</summary>
        private static string BuildAnnotatedPdf(string path)
        {
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(200);
            page.Height = XUnit.FromPoint(200);
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawRectangle(XBrushes.White, new XRect(0, 0, 200, 200));

            // Appearance stream: fill the whole 160x160 box with solid red.
            var ap = new PdfDictionary(doc);
            ap.Elements["/Type"] = new PdfName("/XObject");
            ap.Elements["/Subtype"] = new PdfName("/Form");
            ap.Elements["/FormType"] = new PdfInteger(1);
            var bbox = new PdfArray(doc);
            foreach (var v in new[] { 0, 0, 160, 160 }) bbox.Elements.Add(new PdfReal(v));
            ap.Elements["/BBox"] = bbox;
            ap.Elements["/Resources"] = new PdfDictionary(doc);
            ap.CreateStream(System.Text.Encoding.ASCII.GetBytes("1 0 0 rg 0 0 160 160 re f"));
            doc.Internals.AddObject(ap);

            var annot = new PdfDictionary(doc);
            annot.Elements["/Type"] = new PdfName("/Annot");
            annot.Elements["/Subtype"] = new PdfName("/Square");
            annot.Elements["/F"] = new PdfInteger(4);           // print flag
            var rect = new PdfArray(doc);
            foreach (var v in new[] { 20, 20, 180, 180 }) rect.Elements.Add(new PdfReal(v));
            annot.Elements["/Rect"] = rect;
            var apDict = new PdfDictionary(doc);
            apDict.Elements["/N"] = ap;
            annot.Elements["/AP"] = apDict;
            doc.Internals.AddObject(annot);

            var annots = new PdfArray(doc);
            annots.Elements.Add(annot);
            page.Elements["/Annots"] = annots;

            doc.Save(path);
            return path;
        }

        private static (byte R, byte G, byte B) SampleCentre(string path, RenderFlags flags, bool useFlags)
        {
            // Same discipline the app follows: PDFium is a process-wide singleton.
            var (bgra, w, h) = Scalpel.Services.PdfiumGate.Run(() =>
            {
                using var dr = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(400, 400));
                using var pr = dr.GetPageReader(0);
                var conv = new Docnet.Core.Converters.NaiveTransparencyRemover();
                return (useFlags ? pr.GetImage(conv, flags) : pr.GetImage(conv),
                        pr.GetPageWidth(), pr.GetPageHeight());
            });
            int off = ((h / 2) * w + (w / 2)) * 4;
            return (bgra[off + 2], bgra[off + 1], bgra[off]);
        }

        [Fact]
        public void WithoutTheFlagAnExistingAnnotationIsInvisible()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-annot-{Path.GetRandomFileName()}.pdf");
            try
            {
                BuildAnnotatedPdf(path);
                var c = SampleCentre(path, RenderFlags.RenderAnnotations, useFlags: false);
                // White page: the red annotation is not drawn. This is today's behaviour.
                Assert.True(c.R > 200 && c.G > 200 && c.B > 200,
                    $"expected an unannotated white page, got ({c.R},{c.G},{c.B})");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void WithTheFlagTheAnnotationIsRendered()
        {
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-annot-{Path.GetRandomFileName()}.pdf");
            try
            {
                BuildAnnotatedPdf(path);
                var c = SampleCentre(path, RenderFlags.RenderAnnotations, useFlags: true);
                Assert.True(c.R > 150 && c.G < 100 && c.B < 100,
                    $"expected the red annotation to be drawn, got ({c.R},{c.G},{c.B})");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void RepeatedAnnotationRenderingDoesNotCorruptPdfiumState()
        {
            // Upstream's reported failure was a crash on a LATER native call, so this interleaves
            // annotation rendering with fresh readers and plain geometry calls, many times over.
            // Single-threaded and gated, which is the shape every production render now has;
            // PDFium's thread-affine teardown is tracked separately in PdfiumGateTests.
            string path = Path.Combine(Path.GetTempPath(), $"scalpel-annot-{Path.GetRandomFileName()}.pdf");
            try
            {
                BuildAnnotatedPdf(path);
                var conv = new Docnet.Core.Converters.NaiveTransparencyRemover();

                for (int i = 0; i < 40; i++)
                {
                    Scalpel.Services.PdfiumGate.Run(() =>
                    {
                    using (var dr = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(300, 300)))
                    using (var pr = dr.GetPageReader(0))
                    {
                        _ = pr.GetImage(conv, RenderFlags.RenderAnnotations);
                        _ = pr.GetPageWidth();
                        _ = pr.GetPageHeight();
                        _ = pr.GetCharacters();
                    }

                    // A plain render straight afterwards, which is where a corrupted global state
                    // would surface.
                    using (var dr2 = Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(300, 300)))
                    using (var pr2 = dr2.GetPageReader(0))
                    {
                        _ = pr2.GetImage(conv);
                        _ = pr2.GetPageWidth();
                    }
                    });
                }
            }
            finally
            {
                // PDFium may still be releasing the mapping; a temp file left behind is not a
                // test failure.
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }
}
