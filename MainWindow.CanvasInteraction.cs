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
        // Canvas interaction
        // ============================================================

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_doc is null) return;
            if (sender is Canvas srcCanvas) _activeCanvas = srcCanvas;
            // Don't intercept clicks on an active text editing box
            if (_activeTextBox is not null && e.OriginalSource is DependencyObject src &&
                IsDescendantOf(src, _activeTextBox))
                return;
            // Don't intercept clicks on the crop confirm bar (canvas uses Preview events which
            // tunnel before child Button clicks fire — we must not swallow them here).
            if (_cropConfirmBar is not null && e.OriginalSource is DependencyObject cropSrc &&
                IsDescendantOf(cropSrc, _cropConfirmBar))
                return;
            // Don't intercept clicks on form field overlay controls (TextBox, CheckBox, etc.)
            // — WPF must handle those natively so focus, toggling, and text entry work.
            if (e.OriginalSource is DependencyObject formSrc && IsFormFieldElement(formSrc))
                return;
            // Check if click lands inside a PDF link overlay.
            // We do an explicit bounds check rather than relying on WPF hit-testing through
            // nested transparent canvases, which is unreliable.
            if (_linkOverlays.Count > 0)
            {
                var clickPos = e.GetPosition(_activeCanvas);
                foreach (var lo in _linkOverlays)
                {
                    double lx = Canvas.GetLeft(lo);
                    double ly = Canvas.GetTop(lo);
                    if (clickPos.X >= lx && clickPos.X <= lx + lo.Width &&
                        clickPos.Y >= ly && clickPos.Y <= ly + lo.Height)
                    {
                        var lTarget = lo.Tag is LinkAnnotInfo lai ? lai.Target : lo.Tag;
                        if (lTarget is int tp)
                            PageList.SelectedIndex = tp;
                        else if (lTarget is string u)
                            try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { }
                        e.Handled = true;
                        return;
                    }
                }
            }
            var pos = e.GetPosition(_activeCanvas);
            int pageIdx = _activeCanvas.Tag is int tagPage ? tagPage : PageList.SelectedIndex;
            if (pageIdx < 0) return;

            // Crop corner handles live in the outer panel and have direct MouseLeftButtonDown
            // handlers attached in AddCropHandles() — no detection needed here.

            // Check if click is on any of the four corner resize handles (signature or image)
            if (_resizeHandles.Count > 0 && _selectedAnnotation is PlacedAnnotation rsa)
            {
                foreach (var hd in _resizeHandles)
                {
                    double hx = Canvas.GetLeft(hd), hy = Canvas.GetTop(hd);
                    if (pos.X >= hx && pos.X <= hx + hd.Width &&
                        pos.Y >= hy && pos.Y <= hy + hd.Height)
                    {
                        _isResizingSig = true;
                        _resizeSigStart = pos;
                        _resizeSigStartScale = rsa.Scale;
                        _resizeSigAnnot = rsa;
                        _resizeCorner = hd.Tag as string ?? "SE";
                        // Anchor on the opposite corner so it stays put while the dragged corner moves.
                        double w0 = rsa.SourceWidth * rsa.Scale, h0 = rsa.SourceHeight * rsa.Scale;
                        double ax = rsa.Position.X, ay = rsa.Position.Y;
                        _resizeAnchor = _resizeCorner switch
                        {
                            "NW" => new Point(ax + w0, ay + h0),
                            "NE" => new Point(ax,      ay + h0),
                            "SW" => new Point(ax + w0, ay),
                            _    => new Point(ax,      ay)   // SE
                        };
                        _activeCanvas.CaptureMouse();
                        e.Handled = true;
                        return;
                    }
                }
            }

            switch (_currentTool)
            {
                case EditTool.Select:
                    if (e.ClickCount == 2)
                    {
                        ClearSelection();
                        ClearTextSelection();
                        EditTextAtPosition(pos, pageIdx);
                        e.Handled = true;
                    }
                    else
                    {
                        // Single click: check if hitting a PlacedAnnotation first — select and drag
                        bool hitPlaced = false;
                        if (_annotations.TryGetValue(pageIdx, out var pageAnnotsList))
                        {
                            for (int i = pageAnnotsList.Count - 1; i >= 0; i--)
                            {
                                if (IsDraggable(pageAnnotsList[i]) &&
                                    HitTestAnnotation(pageAnnotsList[i], pos, out Rect paBounds))
                                {
                                    var pa = pageAnnotsList[i];
                                    ClearSelection();
                                    RenderAllAnnotations(pageIdx);
                                    SelectAnnotation(pa, paBounds);
                                    _isDraggingAnnot = true;
                                    _dragAnnotStart = pos;
                                    _dragAnnotOrigPos = AnnotGetPos(pa);
                                    _dragAnnot = pa;
                                    _activeCanvas.CaptureMouse();
                                    e.Handled = true;
                                    hitPlaced = true;
                                    break;
                                }
                            }
                        }
                        if (!hitPlaced)
                        {
                            ClearSelection();
                            ClearTextSelection();
                            _isSelecting = true;
                            _selectStart = pos;
                            _selectRect = new Rectangle
                            {
                                Fill = new SolidColorBrush(Color.FromArgb(40, 74, 130, 255)),
                                Stroke = new SolidColorBrush(Color.FromArgb(120, 74, 130, 255)),
                                StrokeThickness = 1,
                                Width = 0, Height = 0,
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(_selectRect, pos.X);
                            Canvas.SetTop(_selectRect, pos.Y);
                            _activeCanvas.Children.Add(_selectRect);
                            _activeCanvas.CaptureMouse();
                            e.Handled = true;
                        }
                    }
                    break;

                case EditTool.Text:
                    CommitActiveTextBox();
                    PlaceTextBox(pos, pageIdx);
                    e.Handled = true;
                    break;

                case EditTool.Highlight:
                    ClearSelection();
                    _isDrawing = true;
                    _drawStart = pos;
                    var rect = new Rectangle
                    {
                        Fill = new SolidColorBrush(_highlightColor),
                        Width = 0, Height = 0
                    };
                    Canvas.SetLeft(rect, pos.X);
                    Canvas.SetTop(rect, pos.Y);
                    _activeCanvas.Children.Add(rect);
                    _activePreview = rect;
                    _activeCanvas.CaptureMouse();
                    break;

                case EditTool.Draw:
                    ClearSelection();
                    _isDrawing = true;
                    _activeInk = new InkAnnotation { PageIndex = pageIdx, StrokeWidth = _drawWidth };
                    _activeInk.SetColor(_drawColor);
                    _activeInk.Points.Add(pos);
                    var poly = new Polyline
                    {
                        Stroke = new SolidColorBrush(_drawColor),
                        StrokeThickness = _drawWidth,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    poly.Points.Add(pos);
                    _activeCanvas.Children.Add(poly);
                    _activePreview = poly;
                    _activeCanvas.CaptureMouse();
                    break;

                case EditTool.Signature:
                    if (_pendingSignature is not null)
                    {
                        PlaceSignature(pos, pageIdx);
                        e.Handled = true;
                    }
                    else
                    {
                        ShowSignaturePopup();
                    }
                    break;

                case EditTool.Image:
                    PlaceImageFromDialog(pos, pageIdx);
                    e.Handled = true;
                    break;

                case EditTool.Crop:
                    ClearSelection();
                    HideCropConfirmBar();
                    _isDrawing = true;
                    _drawStart = pos;
                    _cropPageIndex = _activeCanvas.Tag is int cpi ? cpi : (_viewMode == ViewMode.Grid ? 0 : PageList.SelectedIndex);
                    _cropPreviewRect = new Rectangle
                    {
                        Stroke          = Brushes.White,
                        StrokeThickness = 1.5,
                        StrokeDashArray = [5, 3],
                        Fill            = AccentBrush(55),
                        Width = 0, Height = 0,
                        IsHitTestVisible = false,
                        Effect = new System.Windows.Media.Effects.DropShadowEffect
                            { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 0.7 },
                    };
                    Canvas.SetLeft(_cropPreviewRect, pos.X);
                    Canvas.SetTop(_cropPreviewRect, pos.Y);
                    Panel.SetZIndex(_cropPreviewRect, 1); // handles sit at ZIndex 10
                    _activeCanvas.Children.Add(_cropPreviewRect);
                    _activePreview = _cropPreviewRect;
                    _activeCanvas.CaptureMouse();
                    break;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Don't interfere with mouse interaction inside form field overlays.
            if (e.OriginalSource is DependencyObject moveSrc && IsFormFieldElement(moveSrc))
                return;

            var pos = e.GetPosition(_activeCanvas);
            pos.X = Math.Max(0, Math.Min(_activeCanvas.ActualWidth, pos.X));
            pos.Y = Math.Max(0, Math.Min(_activeCanvas.ActualHeight, pos.Y));

            // Signature resize drag
            if (_isResizingSig && _resizeSigAnnot is not null)
            {
                // Uniform-scale resize from the dragged corner; the opposite corner (_resizeAnchor)
                // stays fixed. Aspect is preserved by taking whichever axis demands the larger scale.
                double desiredW = Math.Abs(pos.X - _resizeAnchor.X);
                double desiredH = Math.Abs(pos.Y - _resizeAnchor.Y);
                double sw = Math.Max(1.0, _resizeSigAnnot.SourceWidth);
                double sh = Math.Max(1.0, _resizeSigAnnot.SourceHeight);
                double newScale = Math.Max(0.05, Math.Max(desiredW / sw, desiredH / sh));
                _resizeSigAnnot.Scale = newScale;

                double newW = _resizeSigAnnot.SourceWidth * newScale;
                double newH = _resizeSigAnnot.SourceHeight * newScale;
                // Reposition the top-left so the anchor corner is preserved.
                double nx = (_resizeCorner is "NW" or "SW") ? _resizeAnchor.X - newW : _resizeAnchor.X;
                double ny = (_resizeCorner is "NW" or "NE") ? _resizeAnchor.Y - newH : _resizeAnchor.Y;
                _resizeSigAnnot.Position = new Point(nx, ny);

                if (_selectionBorder is not null)
                {
                    _selectionBorder.Width  = newW + 8;
                    _selectionBorder.Height = newH + 8;
                    Canvas.SetLeft(_selectionBorder, nx - 4);
                    Canvas.SetTop(_selectionBorder, ny - 4);
                }
                LayoutResizeHandles(nx, ny, newW, newH);

                // Re-render annotations to show updated size
                RenderAllAnnotations(_resizeSigAnnot.PageIndex);
                // Restore selection visuals (RenderAllAnnotations clears canvas children including our overlays)
                _activeCanvas.Children.Add(_selectionBorder!);
                foreach (var hd in _resizeHandles) _activeCanvas.Children.Add(hd);
                return;
            }

            // Annotation drag-to-move
            if (_isDraggingAnnot && _dragAnnot is not null)
            {
                double dx = pos.X - _dragAnnotStart.X;
                double dy = pos.Y - _dragAnnotStart.Y;
                AnnotSetPos(_dragAnnot, new Point(_dragAnnotOrigPos.X + dx, _dragAnnotOrigPos.Y + dy));
                var db = AnnotBounds(_dragAnnot);
                if (_selectionBorder is not null)
                {
                    Canvas.SetLeft(_selectionBorder, db.X - 4);
                    Canvas.SetTop(_selectionBorder, db.Y - 4);
                }
                if (_dragAnnot is PlacedAnnotation)
                    LayoutResizeHandles(db.X, db.Y, db.Width, db.Height);
                RenderAllAnnotations(_dragAnnot.PageIndex);
                _activeCanvas.Children.Add(_selectionBorder!);
                foreach (var hd in _resizeHandles) _activeCanvas.Children.Add(hd);
                return;
            }

            // Text selection drag
            if (_isSelecting && _selectRect is not null)
            {
                Canvas.SetLeft(_selectRect, Math.Min(pos.X, _selectStart.X));
                Canvas.SetTop(_selectRect, Math.Min(pos.Y, _selectStart.Y));
                _selectRect.Width = Math.Abs(pos.X - _selectStart.X);
                _selectRect.Height = Math.Abs(pos.Y - _selectStart.Y);
                return;
            }

            // Crop corner handle drag — must be before the _isDrawing guard since handle drag
            // runs with _isDrawing = false and _activePreview = null.
            if (_activeCropHandleTag is not null && _cropPreviewRect is not null)
            {
                double dx = pos.X - _cropHandleDragStart.X;
                double dy = pos.Y - _cropHandleDragStart.Y;
                var r = _cropRectAtHandleDrag;
                double newX = r.X, newY = r.Y, newW = r.Width, newH = r.Height;
                switch (_activeCropHandleTag)
                {
                    case "NW":
                        newX = Math.Min(r.Right - 10, r.X + dx);
                        newY = Math.Min(r.Bottom - 10, r.Y + dy);
                        newW = r.Right - newX;
                        newH = r.Bottom - newY;
                        break;
                    case "NE":
                        newY = Math.Min(r.Bottom - 10, r.Y + dy);
                        newW = Math.Max(10, r.Width + dx);
                        newH = r.Bottom - newY;
                        break;
                    case "SE":
                        newW = Math.Max(10, r.Width + dx);
                        newH = Math.Max(10, r.Height + dy);
                        break;
                    case "SW":
                        newX = Math.Min(r.Right - 10, r.X + dx);
                        newW = r.Right - newX;
                        newH = Math.Max(10, r.Height + dy);
                        break;
                }
                _cropCanvasRect = new Rect(newX, newY, newW, newH);
                UpdateCropRectVisuals();
                return;
            }

            if (!_isDrawing || _activePreview is null) return;

            switch (_currentTool)
            {
                case EditTool.Highlight when _activePreview is Rectangle rect:
                    Canvas.SetLeft(rect, Math.Min(pos.X, _drawStart.X));
                    Canvas.SetTop(rect, Math.Min(pos.Y, _drawStart.Y));
                    rect.Width = Math.Abs(pos.X - _drawStart.X);
                    rect.Height = Math.Abs(pos.Y - _drawStart.Y);
                    break;

                case EditTool.Draw when _activePreview is Polyline poly && _activeInk is not null:
                    _activeInk.Points.Add(pos);
                    poly.Points.Add(pos);
                    break;

                case EditTool.Crop when _activePreview is Rectangle crect:
                    Canvas.SetLeft(crect, Math.Min(pos.X, _drawStart.X));
                    Canvas.SetTop(crect, Math.Min(pos.Y, _drawStart.Y));
                    crect.Width = Math.Abs(pos.X - _drawStart.X);
                    crect.Height = Math.Abs(pos.Y - _drawStart.Y);
                    break;
            }
        }

        // Draggable annotations (placed image/signature and typewriter text) expose a top-left
        // Position; these helpers read/write it generically so one drag path serves both.
        private static bool IsDraggable(PageAnnotation a) => a is PlacedAnnotation or TextAnnotation;
        private static Point AnnotGetPos(PageAnnotation a) => a switch
        {
            PlacedAnnotation p => p.Position,
            TextAnnotation t   => t.Position,
            _                  => default
        };
        private static void AnnotSetPos(PageAnnotation a, Point pos)
        {
            switch (a)
            {
                case PlacedAnnotation p: p.Position = pos; break;
                case TextAnnotation t:   t.Position = pos; break;
            }
        }
        private Rect AnnotBounds(PageAnnotation a)
        {
            HitTestAnnotation(a, new Point(double.MinValue, double.MinValue), out Rect b);
            return b;
        }

        // Returns the page index + canvas under the mouse across every per-page overlay
        // (grid / two-page / continuous tiles) and the primary page canvas. Used to drop a placed
        // annotation onto a different page than the one it started on.
        private (int page, Canvas canvas)? PageCanvasUnderPointer(MouseEventArgs e)
        {
            foreach (var kv in _continuousCanvases)
            {
                var c = kv.Value;
                if (c.ActualWidth <= 0 || c.ActualHeight <= 0) continue;
                var p = e.GetPosition(c);
                if (p.X >= 0 && p.X <= c.ActualWidth && p.Y >= 0 && p.Y <= c.ActualHeight)
                    return (kv.Key, c);
            }
            if (_annotationCanvas.ActualWidth > 0 && _annotationCanvas.ActualHeight > 0)
            {
                var pp = e.GetPosition(_annotationCanvas);
                if (pp.X >= 0 && pp.X <= _annotationCanvas.ActualWidth &&
                    pp.Y >= 0 && pp.Y <= _annotationCanvas.ActualHeight)
                    return (_viewMode == ViewMode.Grid ? 0 : PageList.SelectedIndex, _annotationCanvas);
            }
            return null;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Don't process release events that originate inside the crop confirm bar.
            if (_cropConfirmBar is not null && e.OriginalSource is DependencyObject cropSrc &&
                IsDescendantOf(cropSrc, _cropConfirmBar))
                return;

            int pageIdx = PageList.SelectedIndex;

            // Finish crop handle drag
            if (_activeCropHandleTag is not null)
            {
                _activeCropHandleTag = null;
                _activeCanvas.ReleaseMouseCapture();
                return;
            }

            // Finish annotation drag-to-move
            if (_isDraggingAnnot)
            {
                _isDraggingAnnot = false;
                _activeCanvas.ReleaseMouseCapture();
                if (_dragAnnot is not null)
                {
                    var da = _dragAnnot;
                    _dragAnnot = null;
                    int oldPage = da.PageIndex;
                    // Released over a different page? Move it there (position was updated live during drag).
                    var drop = PageCanvasUnderPointer(e);
                    if (drop is { } d && d.page != oldPage && _doc is not null
                        && d.page >= 0 && d.page < _doc.PageCount)
                    {
                        var pt = e.GetPosition(d.canvas);
                        AnnotSetPos(da, new Point(pt.X - (_dragAnnotStart.X - _dragAnnotOrigPos.X),
                                                  pt.Y - (_dragAnnotStart.Y - _dragAnnotOrigPos.Y)));
                        if (_annotations.TryGetValue(oldPage, out var oldList)) oldList.Remove(da);
                        da.PageIndex = d.page;
                        if (!_annotations.TryGetValue(d.page, out var newList)) { newList = []; _annotations[d.page] = newList; }
                        newList.Add(da);
                        ClearSelection();
                        RenderAllAnnotations(oldPage);
                        RenderAllAnnotations(d.page);
                        SelectAnnotation(da, AnnotBounds(da));
                        MarkDirty();
                        return;
                    }
                    RenderAllAnnotations(da.PageIndex);
                    SelectAnnotation(da, AnnotBounds(da));
                    MarkDirty();
                }
                return;
            }

            // Finish signature resize
            if (_isResizingSig)
            {
                _isResizingSig = false;
                _activeCanvas.ReleaseMouseCapture();
                if (_resizeSigAnnot is not null)
                {
                    // Final re-render and re-select to reposition handle cleanly
                    var sa = _resizeSigAnnot;
                    _resizeSigAnnot = null;
                    RenderAllAnnotations(sa.PageIndex);
                    double newW = sa.SourceWidth * sa.Scale;
                    double newH = sa.SourceHeight * sa.Scale;
                    SelectAnnotation(sa, new Rect(sa.Position.X, sa.Position.Y, newW, newH));
                    MarkDirty();
                }
                return;
            }

            // Handle text selection release
            if (_isSelecting)
            {
                _isSelecting = false;
                _activeCanvas.ReleaseMouseCapture();
                var pos = e.GetPosition(_activeCanvas);
                double dragW = Math.Abs(pos.X - _selectStart.X);
                double dragH = Math.Abs(pos.Y - _selectStart.Y);

                if (dragW < 5 && dragH < 5)
                {
                    // Tiny drag = single click -> try annotation selection
                    ClearTextSelection();
                    if (pageIdx >= 0 && _annotations.ContainsKey(pageIdx))
                    {
                        for (int i = _annotations[pageIdx].Count - 1; i >= 0; i--)
                        {
                            if (HitTestAnnotation(_annotations[pageIdx][i], _selectStart, out Rect bounds))
                            {
                                SelectAnnotation(_annotations[pageIdx][i], bounds);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Real drag -> extract text from rectangle
                    var selectBounds = new Rect(
                        Math.Min(pos.X, _selectStart.X), Math.Min(pos.Y, _selectStart.Y),
                        dragW, dragH);
                    ExtractTextFromRegion(pageIdx, selectBounds);
                }
                return;
            }

            if (!_isDrawing) return;
            _isDrawing = false;
            _activeCanvas.ReleaseMouseCapture();

            switch (_currentTool)
            {
                case EditTool.Highlight when _activePreview is Rectangle rect:
                    if (rect.Width > 3 && rect.Height > 3)
                    {
                        var ha = new HighlightAnnotation
                        {
                            PageIndex = pageIdx,
                            Bounds = new Rect(Canvas.GetLeft(rect), Canvas.GetTop(rect), rect.Width, rect.Height)
                        };
                        ha.SetColor(_highlightColor);
                        AddAnnotation(ha);
                    }
                    else
                    {
                        _activeCanvas.Children.Remove(rect);
                    }
                    break;

                case EditTool.Draw when _activeInk is not null:
                    if (_activeInk.Points.Count > 2)
                    {
                        AddAnnotation(_activeInk);
                    }
                    else
                    {
                        _activeCanvas.Children.Remove(_activePreview);
                    }
                    _activeInk = null;
                    break;

                case EditTool.Crop when _activePreview is Rectangle cr:
                    _activeCanvas.ReleaseMouseCapture(); // MUST release before showing handles
                    if (cr.Width > 10 && cr.Height > 10)
                    {
                        _cropCanvasRect = new Rect(Canvas.GetLeft(cr), Canvas.GetTop(cr), cr.Width, cr.Height);
                        _activePreview = null;
                        if (_cropPreviewRect is not null)
                            Panel.SetZIndex(_cropPreviewRect, 1); // below handles (ZIndex 10)
                        ShowCropConfirmBar();
                        return;
                    }
                    else
                    {
                        _activeCanvas.Children.Remove(cr);
                        _cropPreviewRect = null;
                    }
                    break;
            }
            _activePreview = null;
        }

    }
}
