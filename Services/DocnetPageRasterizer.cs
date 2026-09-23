using System;
using System.IO;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Scalpel.Services
{
    /// <summary>
    /// <see cref="IPageRasterizer"/> backed by Docnet/PDFium. Renders each page to a PNG and reads
    /// page point sizes from the document structure. This is the production rasterizer used by
    /// compression, redaction, and OCR. Native — kept out of the unit-test project (the pipeline
    /// logic is tested against a fake rasterizer instead).
    /// </summary>
    public sealed class DocnetPageRasterizer : IPageRasterizer, IDisposable
    {
        private readonly IDocReader _reader;
        private readonly double[] _widthsPt;
        private readonly double[] _heightsPt;


        /// <param name="path">PDF file to rasterize (must be a decrypted/openable file).</param>
        /// <param name="renderLongEdgePx">Target pixels for the longest page edge (render resolution).</param>
        public DocnetPageRasterizer(string path, int renderLongEdgePx = 2000)
        {
            // Every PDFium touch - open, render and dispose alike - happens on the one PDFium
            // thread. Holding that thread for this object's whole lifetime instead would deadlock
            // the streaming render loops, which call Dispatcher.Invoke between pages.
            _reader = Scalpel.Services.PdfiumGate.Run(
                () => Scalpel.Services.PinnedDocReader.Open(path, new PageDimensions(renderLongEdgePx, renderLongEdgePx)));

            int count;
            try
            {
                count = Scalpel.Services.PdfiumGate.Run(() => _reader.GetPageCount());
            }
            catch
            {
                // The reader is open but this object will never exist, so nothing would ever
                // Dispose it. Letting the finalizer do it later tears PDFium down from the GC
                // thread instead of the PDFium thread, which faults - so close it here.
                try { Scalpel.Services.PdfiumGate.Run(() => _reader.Dispose()); } catch { }
                throw;
            }
            _widthsPt = new double[count];
            _heightsPt = new double[count];

            // Default to US Letter if structure can't be read; overwrite with real point sizes.
            for (int i = 0; i < count; i++) { _widthsPt[i] = 612; _heightsPt[i] = 792; }
            try
            {
                using var doc = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
                for (int i = 0; i < count && i < doc.PageCount; i++)
                {
                    _widthsPt[i] = doc.Pages[i].Width.Point;
                    _heightsPt[i] = doc.Pages[i].Height.Point;
                }
            }
            catch { /* keep Letter fallback */ }
        }

        public int PageCount => Scalpel.Services.PdfiumGate.Run(() => _reader.GetPageCount());

        public (double widthPt, double heightPt) PageSizePt(int pageIndex)
            => (_widthsPt[pageIndex], _heightsPt[pageIndex]);

        public RasterPage RenderPage(int pageIndex)
        {
            // Only the native part is marshalled; the PNG encode below is managed work and would
            // just occupy the PDFium thread for no reason.
            var (raw, bw, bh) = Scalpel.Services.PdfiumGate.Run(() =>
            {
                using var pr = _reader.GetPageReader(pageIndex);
                int w = pr.GetPageWidth();
                int h = pr.GetPageHeight();
                // tightly-packed BGRA, top-down
                byte[] pixels = pr.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(),
                                            Scalpel.Services.AnnotationRenderPolicy.ForOutput());
                return (pixels, w, h);
            });

            using var img = Image.LoadPixelData<Bgra32>(raw, bw, bh);
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            return new RasterPage(ms.ToArray(), bw, bh);
        }

        public void Dispose()
        {
            // Teardown is where the crash lands, so it must run on the PDFium thread too.
            Scalpel.Services.PdfiumGate.Run(() => _reader.Dispose());
        }
    }
}
