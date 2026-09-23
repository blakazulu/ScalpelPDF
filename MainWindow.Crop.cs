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
        // Crop tool
        // ============================================================

        // -- Crop coordinate helpers -----------------------------------------
        //
        // The rendered canvas already incorporates the page rotation, so canvas<->PDF
        // conversion is rotation-aware. The maths lives in Services/PageSpaceMap.cs, shared with
        // the form-widget overlay, because this transform was previously hand-derived in both
        // places and the copies disagreed at 180 degrees - where the rotation's vertical flip and
        // the y-up/y-down flip cancel out. The crop copy flipped anyway and wrote a CropBox for
        // the mirrored half of the page. See PageSpaceMapTests.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Convert a canvas-space <see cref="Rect"/> to PDF CropBox coordinates
        /// (bottom-left origin, points) with rotation awareness.
        /// </summary>
        private static (double x1, double y1, double x2, double y2) CanvasToPdfRect(
            Rect cr, double pdfW, double pdfH, double canvasW, double canvasH, int rot)
            => Scalpel.Services.PageSpaceMap.ToPdf(
                   cr.X, cr.Y, cr.Width, cr.Height, pdfW, pdfH, canvasW, canvasH, rot);

        /// <summary>
        /// Inverse of <see cref="CanvasToPdfRect"/> - map PDF CropBox coords back to a canvas-space
        /// <see cref="Rect"/>.
        /// </summary>
        private static Rect PdfToCanvasRect(
            double x1, double y1, double x2, double y2,
            double pdfW, double pdfH, double canvasW, double canvasH, int rot)
        {
            var r = Scalpel.Services.PageSpaceMap.ToCanvas(
                        x1, y1, x2, y2, pdfW, pdfH, canvasW, canvasH, rot);

            // Keep the handle grabbable even if the stored box is degenerate.
            return new Rect(Math.Max(0, r.X), Math.Max(0, r.Y),
                            Math.Max(10, r.W), Math.Max(10, r.H));
        }

        /// <summary>
        /// Push current <see cref="_cropCanvasRect"/> → PDF coords into the x1/y1/x2/y2 TextBoxes.
        /// No-ops when the TextBoxes haven't been created yet (confirm bar not showing).
        /// </summary>
        private void SyncCropBoxInputs()
        {
            if (_cropX1Box is null || _doc is null) return;
            int pi = PageList.SelectedIndex;
            if (pi < 0 || !_renderDims.TryGetValue(pi, out var dims)) return;
            _pageRotations.TryGetValue(pi, out int rot);
            var page = _doc.Pages[pi];
            double pdfW = page.Width.Point, pdfH = page.Height.Point;

            var (x1, y1, x2, y2) = CanvasToPdfRect(_cropCanvasRect, pdfW, pdfH, dims.w, dims.h, rot);
            x1 = Math.Max(0,    x1); y1 = Math.Max(0,    y1);
            x2 = Math.Min(pdfW, x2); y2 = Math.Min(pdfH, y2);

            _updatingCropInputs = true;
            _cropX1Box.Text  = $"{x1:F1}";
            _cropY1Box!.Text = $"{y1:F1}";
            _cropX2Box!.Text = $"{x2:F1}";
            _cropY2Box!.Text = $"{y2:F1}";
            _updatingCropInputs = false;
        }

        /// <summary>
        /// Read x1/y1/x2/y2 TextBoxes → update <see cref="_cropCanvasRect"/> and visuals.
        /// Called on Enter-key or LostFocus inside each TextBox.
        /// </summary>
        private void CommitCropBoxInput()
        {
            if (_updatingCropInputs || _cropX1Box is null || _doc is null) return;
            int pi = PageList.SelectedIndex;
            if (pi < 0 || !_renderDims.TryGetValue(pi, out var dims)) return;
            if (!double.TryParse(_cropX1Box.Text,  out double x1)) return;
            if (!double.TryParse(_cropY1Box!.Text, out double y1)) return;
            if (!double.TryParse(_cropX2Box!.Text, out double x2)) return;
            if (!double.TryParse(_cropY2Box!.Text, out double y2)) return;

            _pageRotations.TryGetValue(pi, out int rot);
            var page = _doc.Pages[pi];
            double pdfW = page.Width.Point, pdfH = page.Height.Point;

            x1 = Math.Max(0,       Math.Min(pdfW - 1, x1));
            y1 = Math.Max(0,       Math.Min(pdfH - 1, y1));
            x2 = Math.Max(x1 + 1,  Math.Min(pdfW,     x2));
            y2 = Math.Max(y1 + 1,  Math.Min(pdfH,     y2));

            _cropCanvasRect = PdfToCanvasRect(x1, y1, x2, y2, pdfW, pdfH, dims.w, dims.h, rot);
            UpdateCropRectVisuals();
        }

        /// <summary>
        /// Parse a page-range string like "1-3,5,7-9" (1-based) into a zero-based index array.
        /// Returns <c>null</c> on parse error or if no valid pages are produced.
        /// </summary>
        private static int[]? ParsePageRange(string input, int pageCount)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var result = new System.Collections.Generic.HashSet<int>();
            foreach (var part in input.Split([','], StringSplitOptions.RemoveEmptyEntries))
            {
                var seg = part.Trim();
                if (seg.Contains('-'))
                {
                    var halves = seg.Split('-');
                    if (halves.Length == 2 &&
                        int.TryParse(halves[0].Trim(), out int lo) &&
                        int.TryParse(halves[1].Trim(), out int hi))
                    {
                        for (int p = lo; p <= hi; p++)
                            if (p >= 1 && p <= pageCount) result.Add(p - 1);
                    }
                    else return null;
                }
                else if (int.TryParse(seg, out int pg))
                {
                    if (pg >= 1 && pg <= pageCount) result.Add(pg - 1);
                }
                else return null;
            }
            return result.Count == 0 ? null : [.. result.OrderBy(x => x)];
        }

        // ─────────────────────────────────────────────────────────────────────

        private void ShowCropConfirmBar()
        {
            // Save the preview rect — HideCropConfirmBar removes it, but we need it to persist.
            var savedRect = _cropPreviewRect;
            _cropPreviewRect = null;
            HideCropConfirmBar();
            _cropPreviewRect = savedRect;
            // Remove fill once confirmed — keep only the outline.
            if (_cropPreviewRect is not null)
                _cropPreviewRect.Fill = Brushes.Transparent;
            if (_doc is null) return;

            int currentPage = _cropPageIndex >= 0 ? _cropPageIndex : PageList.SelectedIndex;
            bool multiPage  = _doc.PageCount > 1;

            var bar = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Padding         = new Thickness(8, 6, 8, 6)
            };
            bar.SetResourceReference(Border.BackgroundProperty,  "BgModal");
            bar.SetResourceReference(Border.BorderBrushProperty, "Accent");

            var outer = new StackPanel { Orientation = Orientation.Vertical };

            // ── Row 1: CropBox coordinate inputs ────────────────────────────────
            var inputRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 5)
            };

            var headerLbl = new TextBlock
            {
                Text              = "CropBox (pts):",
                FontFamily        = (FontFamily)FindResource("FontUI"),
                FontSize          = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 6, 0)
            };
            headerLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            inputRow.Children.Add(headerLbl);

            // Helper: adds a label + TextBox pair to inputRow and returns the TextBox
            TextBox AddCoordField(string lbl)
            {
                var fieldLbl = new TextBlock
                {
                    Text              = lbl,
                    FontFamily        = (FontFamily)FindResource("FontUI"),
                    FontSize          = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 2, 0)
                };
                fieldLbl.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                inputRow.Children.Add(fieldLbl);
                var tb = new TextBox
                {
                    Width             = 52,
                    Height            = 22,
                    FontFamily        = (FontFamily)FindResource("FontUI"),
                    FontSize          = 11,
                    BorderThickness   = new Thickness(1),
                    Padding           = new Thickness(3, 1, 3, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin            = new Thickness(0, 0, 6, 0)
                };
                tb.SetResourceReference(TextBox.BackgroundProperty,  "BgPanel");
                tb.SetResourceReference(TextBox.ForegroundProperty,  "TextPrimary");
                tb.SetResourceReference(TextBox.BorderBrushProperty, "BorderDim");
                tb.KeyDown   += (_, e) => { if (e.Key == Key.Enter) { CommitCropBoxInput(); e.Handled = true; } };
                tb.LostFocus += (_, _) => CommitCropBoxInput();
                inputRow.Children.Add(tb);
                return tb;
            }

            _cropX1Box = AddCoordField("x1:");
            _cropY1Box = AddCoordField("y1:");
            _cropX2Box = AddCoordField("x2:");
            _cropY2Box = AddCoordField("y2:");

            outer.Children.Add(inputRow);

            // ── Row 2: Apply / remove / cancel buttons ───────────────────────────
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Base button style - only non-theme values; theme colors set via
            // SetResourceReference on each instance so they update on theme switches.
            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            btnStyle.Setters.Add(new Setter(Button.PaddingProperty,   new Thickness(8, 3, 8, 3)));
            btnStyle.Setters.Add(new Setter(Button.MarginProperty,    new Thickness(0, 0, 5, 0)));
            btnStyle.Setters.Add(new Setter(Button.CursorProperty,    Cursors.Hand));
            btnStyle.Setters.Add(new Setter(Button.FontFamilyProperty, (FontFamily)FindResource("FontUI")));
            btnStyle.Setters.Add(new Setter(Button.FontSizeProperty,   12.0));

            // Wire up DynamicResource-equivalent bindings on a crop button.
            void StyleAccentBtn(Button b)
            {
                b.SetResourceReference(Button.BackgroundProperty,  "AccentDim");
                b.SetResourceReference(Button.ForegroundProperty,  "Accent");
                b.SetResourceReference(Button.BorderBrushProperty, "Accent");
            }

            // "This Page" apply
            var thisPageBtn = new Button { Content = "This Page", Style = btnStyle,
                ToolTip = "Crop this page (Enter)" };
            StyleAccentBtn(thisPageBtn);
            thisPageBtn.Click += (_, _) => ApplyCrop([currentPage]);
            btnRow.Children.Add(thisPageBtn);

            // Range input + "Range" apply button
            _cropRangeBox = new TextBox
            {
                Width             = 68,
                Height            = 22,
                FontFamily        = (FontFamily)FindResource("FontUI"),
                FontSize          = 11,
                BorderThickness   = new Thickness(1),
                Padding           = new Thickness(3, 1, 3, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 3, 0),
                ToolTip           = "Page range, e.g. 1-3,5"
            };
            _cropRangeBox.SetResourceReference(TextBox.BackgroundProperty,  "BgPanel");
            _cropRangeBox.SetResourceReference(TextBox.ForegroundProperty,  "TextPrimary");
            _cropRangeBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderDim");
            var rangeApplyBtn = new Button { Content = "Range", Style = btnStyle,
                ToolTip = "Crop pages in the specified range" };
            StyleAccentBtn(rangeApplyBtn);
            rangeApplyBtn.Click += (_, _) =>
            {
                int pc = _doc?.PageCount ?? 0;
                var pages = ParsePageRange(_cropRangeBox?.Text ?? "", pc);
                if (pages is null) { SetStatus(Loc("Str_InvalidRange")); return; }
                ApplyCrop(pages);
            };
            btnRow.Children.Add(_cropRangeBox);
            btnRow.Children.Add(rangeApplyBtn);

            if (multiPage)
            {
                var allPagesBtn = new Button { Content = "All Pages", Style = btnStyle };
                StyleAccentBtn(allPagesBtn);
                allPagesBtn.Click += (_, _) => ApplyCrop([.. Enumerable.Range(0, _doc!.PageCount)]);
                btnRow.Children.Add(allPagesBtn);
            }

            // Visual divider before destructive buttons
            var divider = new Border
            {
                Width             = 1,
                Margin            = new Thickness(2, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            divider.SetResourceReference(Border.BackgroundProperty, "BorderDim");
            btnRow.Children.Add(divider);

            // Remove Crop — only shown if current page already has a CropBox
            bool hasCropBox = _doc.Pages[currentPage].Elements.ContainsKey("/CropBox");
            if (hasCropBox)
            {
                var removeBtn = new Button { Content = "Remove Crop", Style = btnStyle,
                    ToolTip = "Remove CropBox (and TrimBox) from this page" };
                removeBtn.SetResourceReference(Button.BackgroundProperty,  "AccentDim");
                removeBtn.SetResourceReference(Button.ForegroundProperty,  "DangerRed");
                removeBtn.SetResourceReference(Button.BorderBrushProperty, "DangerRed");
                removeBtn.Click += (_, _) => RemoveCropBox([currentPage]);
                btnRow.Children.Add(removeBtn);

                if (multiPage)
                {
                    var removeAllBtn = new Button { Content = "Remove All", Style = btnStyle,
                        ToolTip = "Remove CropBox from all pages" };
                    removeAllBtn.SetResourceReference(Button.BackgroundProperty,  "AccentDim");
                    removeAllBtn.SetResourceReference(Button.ForegroundProperty,  "DangerRed");
                    removeAllBtn.SetResourceReference(Button.BorderBrushProperty, "DangerRed");
                    removeAllBtn.Click += (_, _) => RemoveCropBox([.. Enumerable.Range(0, _doc!.PageCount)]);
                    btnRow.Children.Add(removeAllBtn);
                }
            }

            var cancelBtn = new Button { Content = "Cancel", Style = btnStyle, ToolTip = "Cancel (Escape)", Background = Brushes.Transparent };
            cancelBtn.SetResourceReference(Button.ForegroundProperty,  "TextSecondary");
            cancelBtn.SetResourceReference(Button.BorderBrushProperty, "TextSecondary");
            cancelBtn.Click += (_, _) => HideCropConfirmBar();
            btnRow.Children.Add(cancelBtn);

            outer.Children.Add(btnRow);
            bar.Child = outer;
            bar.HorizontalAlignment = HorizontalAlignment.Left;
            bar.VerticalAlignment   = VerticalAlignment.Top;
            bar.Cursor              = Cursors.SizeAll;
            // *** Place bar in the OUTER (unscaled) grid so it renders at native
            // screen size regardless of the current zoom level. ***
            Panel.SetZIndex(bar, 100);

            // ── Drag-to-move ─────────────────────────────────────────────────────
            bar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is Button || e.OriginalSource is TextBox) return;
                _cropBarDragging   = true;
                _cropBarDragOffset = e.GetPosition(PagePreviewPanel.Parent as UIElement);
                _cropBarDragOffset = new Point(
                    _cropBarDragOffset.X - bar.Margin.Left,
                    _cropBarDragOffset.Y - bar.Margin.Top);
                bar.CaptureMouse();
                e.Handled = true;
            };
            bar.MouseMove += (s, e) =>
            {
                if (!_cropBarDragging) return;
                var pos = e.GetPosition(PagePreviewPanel.Parent as UIElement);
                bar.Margin = new Thickness(
                    Math.Max(0, pos.X - _cropBarDragOffset.X),
                    Math.Max(0, pos.Y - _cropBarDragOffset.Y), 0, 0);
                e.Handled = true;
            };
            bar.MouseLeftButtonUp += (s, e) =>
            {
                if (!_cropBarDragging) return;
                _cropBarDragging = false;
                bar.ReleaseMouseCapture();
                e.Handled = true;
            };
            // ─────────────────────────────────────────────────────────────────────

            _cropConfirmBar = bar;

            var outerGrid = PagePreviewPanel.Parent as Panel ?? (Panel)_annotationCanvas;
            outerGrid.Children.Add(bar);
            AddCropHandles();
            RepositionCropConfirmBar();
            SyncCropBoxInputs();
        }

        private void HideCropConfirmBar()
        {
            if (_cropConfirmBar is not null)
            {
                // Remove from whichever panel it was added to (outer grid or canvas fallback)
                (_annotationCanvas.Parent as Panel)?.Children.Remove(_cropConfirmBar);
                _annotationCanvas.Children.Remove(_cropConfirmBar);   // no-op if not there
                (PagePreviewPanel.Parent as Panel)?.Children.Remove(_cropConfirmBar);
                _cropConfirmBar = null;
            }
            if (_cropPreviewRectBorder is not null)
            {
                (_cropPreviewRectBorder.Parent as Panel)?.Children.Remove(_cropPreviewRectBorder);
                _annotationCanvas.Children.Remove(_cropPreviewRectBorder);
                _cropPreviewRectBorder = null;
            }
            if (_cropPreviewRect is not null)
            {
                (_cropPreviewRect.Parent as Panel)?.Children.Remove(_cropPreviewRect);
                _annotationCanvas.Children.Remove(_cropPreviewRect);
                _cropPreviewRect = null;
            }
            RemoveCropHandles();
            _cropX1Box = _cropY1Box = _cropX2Box = _cropY2Box = null;
            _cropRangeBox = null;
        }

        private void AddCropHandles()
        {
            RemoveCropHandles();
            const double hSize = 24;
            var tags    = new[] { "NW", "NE", "SE", "SW" };
            var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNWSE, Cursors.SizeNESW };
            // Handles live in the OUTER unscaled panel (same as the confirm bar) so they render
            // at a fixed screen size regardless of canvas zoom level.
            var outerGrid = PagePreviewPanel.Parent as Panel ?? (Panel)_annotationCanvas;

            for (int i = 0; i < 4; i++)
            {
                var tag = tags[i];
                var h = new Rectangle
                {
                    Width  = hSize, Height = hSize,
                    Fill   = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    StrokeThickness = 1.5,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                        { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 0.6 },
                    Tag    = tag,
                    Cursor = cursors[i],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment   = VerticalAlignment.Top,
                };
                Panel.SetZIndex(h, 101);
                // Attach drag directly on the handle so clicks don't need to reach _annotationCanvas.
                h.MouseLeftButtonDown += (_, e) =>
                {
                    _activeCropHandleTag  = tag;
                    // Measure and capture against the active surface (per-page overlay in
                    // Continuous view) so the drag delta matches the crop rect's coordinate space.
                    _cropHandleDragStart  = e.GetPosition(_activeCanvas);
                    _cropRectAtHandleDrag = _cropCanvasRect;
                    _activeCanvas.CaptureMouse();
                    e.Handled = true;
                };
                _cropHandles.Add(h);
                outerGrid.Children.Add(h);
            }
            PositionCropHandles();
        }

        private void RemoveCropHandles()
        {
            var outerGrid = PagePreviewPanel.Parent as Panel ?? (Panel)_annotationCanvas;
            foreach (var h in _cropHandles)
            {
                outerGrid.Children.Remove(h);
                _annotationCanvas.Children.Remove(h); // belt-and-suspenders in case it ended up in canvas
            }
            _cropHandles.Clear();
            _activeCropHandleTag = null;
            RemoveCropBrackets();   // no-op — list is always empty now, kept for safety
        }

        private void RemoveCropBrackets()
        {
            foreach (var b in _cropBrackets) _annotationCanvas.Children.Remove(b);
            _cropBrackets.Clear();
        }

        private void PositionCropHandles()
        {
            if (_cropHandles.Count < 4) return;
            const double hSize = 24;
            var outerGrid = PagePreviewPanel.Parent as UIElement ?? _annotationCanvas;
            // Translate canvas-space corners to outer-panel screen space (same as RepositionCropConfirmBar).
            var canvasCorners = new Point[]
            {
                new(_cropCanvasRect.X,      _cropCanvasRect.Y),
                new(_cropCanvasRect.Right,   _cropCanvasRect.Y),
                new(_cropCanvasRect.Right,   _cropCanvasRect.Bottom),
                new(_cropCanvasRect.X,       _cropCanvasRect.Bottom),
            };
            var offsets = new (double dx, double dy)[]
            {
                (0,       0      ),   // NW: top-left at top-left corner
                (-hSize,  0      ),   // NE: top-right at top-right corner
                (-hSize, -hSize  ),   // SE: bottom-right at bottom-right corner
                (0,      -hSize  ),   // SW: bottom-left at bottom-left corner
            };
            for (int i = 0; i < 4; i++)
            {
                Point screen = _activeCanvas.TranslatePoint(canvasCorners[i], outerGrid);
                _cropHandles[i].Margin = new Thickness(
                    screen.X + offsets[i].dx,
                    screen.Y + offsets[i].dy,
                    0, 0);
            }
        }

        private void UpdateCropRectVisuals()
        {
            if (_cropPreviewRect is null) return;
            var r = _cropCanvasRect;
            Canvas.SetLeft(_cropPreviewRect, r.X); Canvas.SetTop(_cropPreviewRect, r.Y);
            _cropPreviewRect.Width = r.Width;       _cropPreviewRect.Height = r.Height;
            PositionCropHandles();
            RepositionCropConfirmBar();
            SyncCropBoxInputs();
        }

        private void RepositionCropConfirmBar()
        {
            if (_cropConfirmBar is null) return;

            // The confirm bar lives in the OUTER (unscaled) panel, so we must
            // translate canvas-space coordinates to that panel's coordinate space.
            var outerGrid = PagePreviewPanel.Parent as UIElement ?? _annotationCanvas;
            Point topLeft     = _activeCanvas.TranslatePoint(
                new Point(_cropCanvasRect.X, _cropCanvasRect.Y), outerGrid);
            Point bottomLeft  = _activeCanvas.TranslatePoint(
                new Point(_cropCanvasRect.X, _cropCanvasRect.Bottom), outerGrid);

            const double barHeight = 78;
            double barLeft     = Math.Max(4, topLeft.X);
            double barTopBelow = bottomLeft.Y + 8;
            double barTopAbove = topLeft.Y - barHeight - 8;

            double parentH = (outerGrid as FrameworkElement)?.ActualHeight
                             ?? _annotationCanvas.ActualHeight;
            double barTop = barTopBelow + barHeight < parentH
                ? barTopBelow : Math.Max(4, barTopAbove);

            _cropConfirmBar.Margin = new Thickness(barLeft, barTop, 0, 0);
        }

        private void ApplyCrop(int[] pageIndices)
        {
            if (_doc is null || _currentFile is null) { SetStatus(Loc("Str_CropNoDoc")); return; }
            int currentPage = _cropPageIndex >= 0 ? _cropPageIndex : PageList.SelectedIndex;
            if (currentPage < 0) { SetStatus(Loc("Str_CropNoPage")); return; }
            if (!_renderDims.TryGetValue(currentPage, out var refDims))
            { SetStatus(Loc("Str_CropNoDims")); return; }

            try
            {
                PushDocUndo();

                // Convert canvas rect to PDF CropBox coords using the rotation-aware helper.
                // This is the correct inversion of how Docnet renders the rotated bitmap.
                _pageRotations.TryGetValue(currentPage, out int rot);
                var refPage = _doc.Pages[currentPage];
                double refPdfW = refPage.Width.Point;
                double refPdfH = refPage.Height.Point;

                var (rx1, ry1, rx2, ry2) = CanvasToPdfRect(
                    _cropCanvasRect, refPdfW, refPdfH, refDims.w, refDims.h, rot);

                foreach (int pi in pageIndices)
                {
                    if (pi < 0 || pi >= _doc.PageCount) continue;
                    var page  = _doc.Pages[pi];
                    double pW = page.Width.Point;
                    double pH = page.Height.Point;

                    // Scale proportionally when "All Pages" spans pages of different sizes
                    double x1 = rx1 * pW / refPdfW;
                    double y1 = ry1 * pH / refPdfH;
                    double x2 = rx2 * pW / refPdfW;
                    double y2 = ry2 * pH / refPdfH;

                    // Clamp to media box and ensure minimum 1-pt size
                    x1 = Math.Max(0, x1);  y1 = Math.Max(0, y1);
                    x2 = Math.Min(pW, x2); y2 = Math.Min(pH, y2);
                    if (x2 - x1 < 1) x2 = x1 + 1;
                    if (y2 - y1 < 1) y2 = y1 + 1;

                    // Write CropBox directly into the page dictionary (more reliable across
                    // PdfSharpCore versions than the CropBox property setter).
                    var cropArr = new PdfSharpCore.Pdf.PdfArray();
                    cropArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(x1));
                    cropArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(y1));
                    cropArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(x2));
                    cropArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(y2));
                    page.Elements["/CropBox"] = cropArr;

                    // Mirror to TrimBox (PDF spec: TrimBox ⊆ CropBox ⊆ MediaBox)
                    var trimArr = new PdfSharpCore.Pdf.PdfArray();
                    trimArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(x1));
                    trimArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(y1));
                    trimArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(x2));
                    trimArr.Elements.Add(new PdfSharpCore.Pdf.PdfReal(y2));
                    page.Elements["/TrimBox"] = trimArr;
                }

                HideCropConfirmBar();
                SetTool(EditTool.Select);
                SaveTempAndReload(keepAnnotations: true);
                SetStatus(string.Format(Loc("Str_Cropped"), pageIndices.Length));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(Loc("Str_CropFailed"), ex.Message));
            }
        }

        private void RemoveCropBox(int[] pageIndices)
        {
            if (_doc is null || _currentFile is null) return;
            try
            {
                PushDocUndo();
                foreach (int pi in pageIndices)
                {
                    if (pi < 0 || pi >= _doc.PageCount) continue;
                    _doc.Pages[pi].Elements.Remove("/CropBox");
                    _doc.Pages[pi].Elements.Remove("/TrimBox");
                }
                HideCropConfirmBar();
                SetTool(EditTool.Select);
                SaveTempAndReload(keepAnnotations: true);
                SetStatus(string.Format(Loc("Str_RemovedCrop"), pageIndices.Length));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(Loc("Str_RemoveCropFailed"), ex.Message));
            }
        }

    }
}
