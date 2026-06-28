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
        // View Mode
        // ============================================================

        private void SetViewMode(ViewMode mode)
        {
            if (_viewMode == mode) return;
            _viewMode = mode;
            App.SetSetting("ViewMode", mode.ToString());

            bool isContinuous = mode == ViewMode.Continuous;
            _pageContentPanel.Visibility = isContinuous ? Visibility.Collapsed : Visibility.Visible;
            _continuousPanel.Visibility  = isContinuous ? Visibility.Visible   : Visibility.Collapsed;

            if (!isContinuous)
            {
                _continuousRenderCts?.Cancel();
                _continuousPanel.Children.Clear();
                _continuousTops.Clear();
                _continuousCanvases.Clear();
            }

            UpdateViewModeButtons();
            if (_doc is null) return;
            int idx = PageList.SelectedIndex;
            if (mode == ViewMode.Continuous)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => SetupContinuousView(idx));
            }
            else
            {
                _secondaryRenderCts?.Cancel();
                ClearSecondaryPages();
                _pageContentPanel.Width = double.NaN;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    RenderPage(mode == ViewMode.Grid ? 0 : idx);
                    // Grid: apply a clean column-fit zoom (continuous's zoom is far too large for a
                    // grid, and a non-column zoom leaves a gap). SetZoom -> ApplyZoom defers the
                    // single tile render, so return here instead of calling RefreshPageView again
                    // (a second render would duplicate tiles).
                    if (mode == ViewMode.Grid)
                    {
                        SetZoom(GridZoomForN(Math.Min(_doc!.PageCount, 3)));
                        return;
                    }
                    // Switching into Single or Two-Page fits the whole page so it isn't left at an
                    // awkward carried-over zoom from another mode.
                    if      (mode == ViewMode.Single || mode == ViewMode.TwoPage) FitToPage();
                    else if (_fitMode == FitMode.Width) FitToWidth();
                    else if (_fitMode == FitMode.Page)  FitToPage();
                    else                                ApplyZoom();
                    RefreshPageView(idx);
                });
            }
        }

        private void ScrollContinuousToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _continuousTops.Count) return;
            double target = _continuousTops[pageIndex] * _zoomLevel;
            PagePreviewPanel.ScrollToVerticalOffset(target);
        }

        private void PagePreviewPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_viewMode != ViewMode.Continuous || _continuousTops.Count == 0) return;

            double viewportCenter = (PagePreviewPanel.VerticalOffset + PagePreviewPanel.ViewportHeight * 0.5)
                                    / Math.Max(0.01, _zoomLevel);
            int nearest = 0;
            double minDist = double.MaxValue;
            for (int i = 0; i < _continuousTops.Count; i++)
            {
                if (i >= _continuousPanel.Children.Count) break;
                var slot = (FrameworkElement)_continuousPanel.Children[i];
                double center = _continuousTops[i] + slot.Height * 0.5;
                double dist   = Math.Abs(center - viewportCenter);
                if (dist < minDist) { minDist = dist; nearest = i; }
            }

            if (PageList.SelectedIndex != nearest)
            {
                _pageJumpBox.Text = (nearest + 1).ToString();
                // Update sidebar thumbnail without triggering a full page render
                PageList.SelectionChanged -= PageList_SelectionChanged;
                PageList.SelectedIndex = nearest;
                PageList.SelectionChanged += PageList_SelectionChanged;
            }
        }

        private void SetupContinuousView(int initialPage)
        {
            if (_doc is null) return;
            _continuousRenderCts?.Cancel();
            _continuousPanel.Children.Clear();
            _continuousTops.Clear();
            _continuousCanvases.Clear();

            // Use the PDF's natural page width in WPF DIPs (96 DIP/inch, 72 pt/inch).
            // This is zoom-independent, which is critical: FitToWidth computes
            //   zoom = viewportW / _continuousPageW
            // and if _continuousPageW were derived from the current zoom level the two
            // would cancel and FitToWidth would always return approximately the old zoom.
            var refPage = _doc.Pages[0];
            _continuousPageW = Math.Max(200.0, refPage.Width.Point * (96.0 / 72.0));

            double y = 0;
            for (int i = 0; i < _doc.PageCount; i++)
            {
                _continuousTops.Add(y);
                var pdfPage = _doc.Pages[i];
                double pw = pdfPage.Width.Point, ph = pdfPage.Height.Point;
                if (_pageRotations.TryGetValue(i, out int prot) && (prot == 90 || prot == 270))
                    (pw, ph) = (ph, pw);
                double ratio = Math.Max(0.1, ph / Math.Max(1, pw));
                double slotH = _continuousPageW * ratio;

                // Canonical render-dim space (matches single-page RenderPage: longest side -> 2048)
                // so annotation coordinates are identical in both view modes.
                double maxDim = Math.Max(pw, ph);
                int rdW = Math.Max(1, (int)Math.Round(2048.0 * pw / maxDim));
                int rdH = Math.Max(1, (int)Math.Round(2048.0 * ph / maxDim));
                _renderDims[i] = (rdW, rdH);

                // Per-page annotation overlay: sized in render-dim space, scaled to the slot.
                double slotScale = _continuousPageW / rdW;
                var overlay = new Canvas
                {
                    Width           = rdW,
                    Height          = rdH,
                    Background       = Brushes.Transparent,
                    ClipToBounds     = true,
                    Tag              = i,
                    LayoutTransform  = new System.Windows.Media.ScaleTransform(slotScale, slotScale)
                };
                overlay.PreviewMouseLeftButtonDown += Canvas_MouseLeftButtonDown;
                overlay.MouseMove                  += Canvas_MouseMove;
                overlay.PreviewMouseLeftButtonUp   += Canvas_MouseLeftButtonUp;
                _continuousCanvases[i] = overlay;

                var pageImg = new Image { Stretch = Stretch.None, Width = _continuousPageW, Height = slotH };
                RenderOptions.SetBitmapScalingMode(pageImg, BitmapScalingMode.HighQuality);

                var slotGrid = new Grid();
                slotGrid.Children.Add(pageImg);
                slotGrid.Children.Add(overlay);

                var placeholder = new Border
                {
                    Width      = _continuousPageW,
                    Height     = slotH,
                    Margin     = new Thickness(0, 0, 0, 12),
                    Background = Application.Current.TryFindResource("Background") as SolidColorBrush
                                 ?? new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Tag = i,
                    Child = slotGrid
                };
                int capturedI = i;
                placeholder.PreviewMouseLeftButtonDown += (_, _) => PageList.SelectedIndex = capturedI;
                _continuousPanel.Children.Add(placeholder);
                y += slotH + 12;
            }

            // Re-apply fit mode now that _continuousPageW is known; default to fit-page (one whole
            // page in view) unless the user explicitly chose fit-width.
            if (_fitMode == FitMode.Width) FitToWidth(); else FitToPage();

            _continuousScrollTarget = initialPage;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                () => ScrollContinuousToPage(initialPage));

            _ = RenderContinuousPages();
        }

        private async System.Threading.Tasks.Task RenderContinuousPages()
        {
            if (_doc is null || _currentFile is null) return;
            _continuousRenderCts?.Cancel();
            _continuousRenderCts = new System.Threading.CancellationTokenSource();
            var cts = _continuousRenderCts;

            string currentFile = _currentFile;
            int pageCount      = _doc.PageCount;
            double targetW     = _continuousPageW;
            int renderW        = Math.Max(800, Math.Min(2048, (int)(targetW * 2)));

            // Capture per-page rotations on the UI thread before going async
            var rotations = new Dictionary<int, int>(_pageRotations);

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var docReader = DocLib.Instance.GetDocReader(
                        currentFile, new PageDimensions(renderW, renderW * 2));

                    for (int i = 0; i < pageCount; i++)
                    {
                        if (cts.IsCancellationRequested) return;
                        using var pr = docReader.GetPageReader(i);
                        int w = pr.GetPageWidth();
                        int h = pr.GetPageHeight();
                        var raw = pr.GetImage();
                        if (w <= 0 || h <= 0 || raw is null) continue;
                        if (rotations.TryGetValue(i, out int rot) && rot != 0)
                            (raw, w, h) = RotateBitmap(raw, w, h, rot);

                        int fi = i, fw = w, fh = h;
                        byte[] bytes = raw;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (cts.IsCancellationRequested || _viewMode != ViewMode.Continuous) return;
                            if (fi >= _continuousPanel.Children.Count) return;

                            var slot = (Border)_continuousPanel.Children[fi];
                            double dipW = slot.Width;
                            double dipH = dipW * fh / fw;
                            double dpiX = 96.0 * fw / dipW;
                            double dpiY = 96.0 * fh / dipH;

                            var bmp = new WriteableBitmap(fw, fh, dpiX, dpiY, PixelFormats.Bgra32, null);
                            bmp.WritePixels(new Int32Rect(0, 0, fw, fh), bytes, fw * 4, 0);
                            bmp.Freeze();

                            if (slot.Child is Grid slotGrid && slotGrid.Children.Count > 0
                                && slotGrid.Children[0] is Image pageImg)
                            {
                                pageImg.Source  = bmp;
                                pageImg.Width   = dipW;
                                pageImg.Height  = dipH;
                                slot.Background = Brushes.White;

                                // Size the slot and overlay from the ACTUAL rendered page so a
                                // cropped page (which renders shorter than its MediaBox estimate)
                                // fills its slot with no white bars. Mirrors single-page view.
                                slot.Height = dipH;
                                double maxF = Math.Max(fw, fh);
                                int rdW = Math.Max(1, (int)Math.Round(2048.0 * fw / maxF));
                                int rdH = Math.Max(1, (int)Math.Round(2048.0 * fh / maxF));
                                _renderDims[fi] = (rdW, rdH);
                                if (slotGrid.Children.Count > 1 && slotGrid.Children[1] is Canvas ov)
                                {
                                    ov.Width  = rdW;
                                    ov.Height = rdH;
                                    ov.LayoutTransform =
                                        new System.Windows.Media.ScaleTransform(dipW / rdW, dipW / rdW);
                                }

                                // Slot heights are now exact; recompute scroll offsets from them.
                                double yy = 0;
                                for (int k = 0; k < _continuousPanel.Children.Count && k < _continuousTops.Count; k++)
                                {
                                    _continuousTops[k] = yy;
                                    double hk = ((FrameworkElement)_continuousPanel.Children[k]).Height;
                                    if (double.IsNaN(hk)) hk = 0;
                                    yy += hk + 12;
                                }

                                // Pages render in order, so when the target page is reached every
                                // page above it has its final height; re-scroll so a crop lands you
                                // back on the same page instead of drifting to the next one.
                                if (_continuousScrollTarget >= 0 && fi >= _continuousScrollTarget)
                                {
                                    int tgt = _continuousScrollTarget;
                                    _continuousScrollTarget = -1;
                                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                                        (Action)(() => ScrollContinuousToPage(tgt)));
                                }

                                RenderAllAnnotations(fi);
                            }
                        });
                    }
                }
                catch { /* render cancelled or doc closed */ }
            }, cts.Token);
        }
    }
}
