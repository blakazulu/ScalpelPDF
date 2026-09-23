using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Straighten a photographed page
        // ============================================================

        /// <summary>
        /// Undoes the perspective on a photographed page: the user marks the four corners of the
        /// document in the photo and the page is replaced with an upright rectangle.
        /// <para>Photographed pages are the common case for anything not scanned - a receipt, a
        /// signed page snapped on a phone - and they are skewed enough that OCR and printing both
        /// suffer. This only touches the one page, so a mixed document keeps everything else.</para>
        /// </summary>
        private void ToolsStraighten_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireOpenDoc()) return;

            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) pageIdx = 0;
            if (pageIdx >= _doc!.PageCount) return;

            // No await in this handler (the corner picker is a modal ShowDialog), so a tab switch
            // cannot actually interleave - the gate is taken anyway for consistency with the other
            // long operations and in case the picker or rasterization step grows an async path later.
            using var op = _longOps.Begin();

            BitmapSource pageImage;
            string source;
            try
            {
                source = BuildWorkingSourceFile();
                // A generous render: the warp resamples, so starting sharp keeps the result sharp.
                using var rast = new DocnetPageRasterizer(source, 2400);
                if (pageIdx >= rast.PageCount) return;
                var raster = rast.RenderPage(pageIdx);
                pageImage = DecodePng(raster.ImageBytes);
            }
            catch (Exception ex)
            {
                Logger.Error("Tools", "straighten.render.fail", ex.Message, ex);
                ScalpelDialog.Show(this, string.Format(Loc("Str_Straighten_Failed"), ex.Message),
                                   Loc("Str_Straighten_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var picker = new StraightenWindow(this, pageImage, Loc);
            if (picker.ShowDialog() != true || picker.Result is null) return;

            try
            {
                ApplyStraightenedPage(pageIdx, picker.Result);
                SetStatus(string.Format(Loc("Str_Straighten_Done"), pageIdx + 1));
            }
            catch (Exception ex)
            {
                Logger.Error("Tools", "straighten.apply.fail", ex.Message, ex);
                ScalpelDialog.Show(this, string.Format(Loc("Str_Straighten_Failed"), ex.Message),
                                   Loc("Str_Straighten_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Replaces one page's content with the straightened bitmap, keeping the page's own size
        /// so the rest of the document still prints on the same paper.
        /// </summary>
        private void ApplyStraightenedPage(int pageIdx, BitmapSource straightened)
        {
            // Pinned so the straightened page lands back in the document that started the tool,
            // matching the pattern the async long operations use.
            var session = _s;
            PushDocUndo();

            string pngPath = App.MakeTempFile("straight", session.Id);
            pngPath = Path.ChangeExtension(pngPath, ".png");
            using (var fs = File.Create(pngPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(straightened));
                encoder.Save(fs);
            }

            var page = _doc!.Pages[pageIdx];
            double w = page.Width.Point, h = page.Height.Point;

            // Drop the old content entirely: what was photographed is being replaced, not
            // annotated, so leaving the skewed original underneath would show through.
            page.Contents.Elements.Clear();

            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Replace))
            {
                var img = XImage.FromFile(pngPath);
                // Fit the straightened image inside the page, centred, preserving its aspect.
                double scale = Math.Min(w / img.PixelWidth, h / img.PixelHeight);
                double dw = img.PixelWidth * scale, dh = img.PixelHeight * scale;
                gfx.DrawImage(img, (w - dw) / 2, (h - dh) / 2, dw, dh);
            }

            string outPath = App.MakeTempFile("straightened", session.Id);
            PdfSaveGuard.Save(_doc, outPath);
            AdoptTransformedFile(session, outPath, string.Format(Loc("Str_Straighten_Done"), pageIdx + 1));
        }
    }
}
