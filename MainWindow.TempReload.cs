using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Scalpel.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Bitmap rotation helper
        // ============================================================

        /// <summary>
        /// Rotates a raw BGRA (4 bytes/pixel) bitmap clockwise by degrees.
        /// Used because Docnet's FPDF_RenderPageBitmapWithMatrix uses a pure-scaling
        /// matrix, so PDFium renders the page in its MediaBox orientation (no rotation).
        /// We strip /Rotate from the temp file so content is never clipped, then rotate
        /// the pixel buffer here to match the intended visual orientation.
        /// </summary>
        internal static (byte[] bytes, int w, int h) RotateBitmapStatic(byte[] src, int w, int h, int degrees)
            => RotateBitmap(src, w, h, degrees);

        private static (byte[] bytes, int w, int h) RotateBitmap(byte[] src, int w, int h, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return (src, w, h);
            int newW = (degrees == 90 || degrees == 270) ? h : w;
            int newH = (degrees == 90 || degrees == 270) ? w : h;
            byte[] dst = new byte[newW * newH * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = (y * w + x) * 4;
                    int dstX, dstY;
                    switch (degrees)
                    {
                        case 90:  dstX = h - 1 - y; dstY = x;         break; // CW
                        case 180: dstX = w - 1 - x; dstY = h - 1 - y; break;
                        default:  dstX = y;          dstY = w - 1 - x; break; // 270 CW
                    }
                    int dstIdx = (dstY * newW + dstX) * 4;
                    dst[dstIdx]     = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
            return (dst, newW, newH);
        }

        // ============================================================
        // Temp save/reload
        // ============================================================

        private void SaveTempAndReload(bool keepAnnotations = false)
        {
            if (_doc is null || _currentFile is null) return;
            // Overlay annotations are unsaved, still-editable user work. Callers that don't change
            // page identity (crop) pass keepAnnotations:true so annotations on other pages survive
            // the reload and stay selectable/movable; they are re-rendered after the doc reopens.
            if (!keepAnnotations) _annotations.Clear();
            _renderDims.Clear();
            InvalidateBlankCache();
            ClearSelection();
            MarkDirty();
            var doc = _doc;
            int selectedIdx = PageList.SelectedIndex;

            // Capture page rotations, then strip them from the document before saving.
            // Docnet uses FPDF_GetPageWidth/Height (MediaBox, no rotation) to size the bitmap,
            // then renders with PDFium's page CTM which *does* include /Rotate.  For 90°/270°
            // the rendered landscape content overflows the portrait-sized bitmap and gets clipped.
            // Stripping /Rotate to 0 before saving means Docnet renders clean unrotated content
            // that fits the bitmap; RotateBitmap is applied in each render path instead.
            _pageRotations.Clear();
            for (int i = 0; i < doc.PageCount; i++)
            {
                int rot = ((doc.Pages[i].Rotate % 360) + 360) % 360;
                _pageRotations[i] = rot;
                Scalpel.Services.PageRotation.Set(doc.Pages[i], 0);
            }

            var tempPath = App.MakeTempFile("temp", _s.Id);
            try
            {
                Scalpel.Services.PdfSaveGuard.Save(doc, tempPath);
                doc.Close();
            }
            catch (Exception saveEx) when (IsXRefException(saveEx))
            {
                // PdfSharpCore fails to re-save encrypted PDFs (e.g. owner-restricted RC4 files)
                // because it encounters cross-reference tokens while serialising dirty objects.
                // Primary fallback: use PDFium (already initialised for the page preview) to
                // load the source, strip all /Rotate values, remove encryption, and save.
                // Secondary fallback: PdfSharpCore Import mode (works on some non-encrypted xref
                // issues but fails on encrypted files; kept as a last resort).
                doc.Close();
                _doc = null;
                if (!TryPdfiumSaveWithZeroRotations(_currentFile!, tempPath) &&
                    !TryImportRepairToPath(_currentFile!, tempPath, stripRotations: true))
                    throw; // re-throw original if both fallbacks fail
            }
            // PdfSharpCore sometimes saves a file where one object's xref offset points at the
            // xref table itself (object N offset = xref table position). When PdfSharp then tries
            // to re-open that file in Modify mode it seeks to the xref table, reads the keyword
            // "xref" as a token in an object context, and throws "Unexpected token 'xref'".
            // Fix: catch the reopen failure, pipe the saved file through PDFium (which has
            // robust error recovery and will rewrite a correct xref), then retry the open.
            try
            {
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            catch (Exception openEx) when (IsXRefException(openEx))
            {
                var fixedPath = App.MakeTempFile("fixed", _s.Id);
                if (!TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                    throw; // PDFium also failed — re-throw original reopen error
                tempPath = fixedPath;
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
            }
            _currentFile = tempPath;

            // Restore rotations in the reopened in-memory doc so saves, form fields,
            // and all other operations see the correct rotation values.
            foreach (var kv in _pageRotations)
                Scalpel.Services.PageRotation.Set(_doc.Pages[kv.Key], kv.Value);

            RefreshPageList();
            if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                PageList.SelectedIndex = selectedIdx;
            else if (PageList.Items.Count > 0)
                PageList.SelectedIndex = 0;

            // In Continuous view the strip caches one rendered slot per page. After a
            // page-modifying reload (e.g. crop) it must be rebuilt so the main view reflects the
            // new pages; the slot-sizing in RenderContinuousPages makes cropped pages fit cleanly.
            if (_viewMode == ViewMode.Continuous)
            {
                int contIdx = PageList.SelectedIndex;
                int contGen = _sessionGeneration;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    (Action)(() => { if (!IsStale(contGen)) SetupContinuousView(contIdx); }));
                return;
            }

            // Refit synchronously so the first rendered frame uses the correct zoom.
            PagePreviewPanel.ScrollToHorizontalOffset(0);
            ReapplyGridOrFit();

            // Deferred refit after layout settles for accurate ActualWidth.
            int gen = _sessionGeneration;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
            {
                if (IsStale(gen)) return;   // another document is shown now
                PagePreviewPanel.ScrollToHorizontalOffset(0);
                ReapplyGridOrFit();
            }));
        }

    }
}
