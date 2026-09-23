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
        // Selection
        // ============================================================

        private bool HitTestAnnotation(PageAnnotation annot, Point pos, out Rect bounds)
        {
            switch (annot)
            {
                case HighlightAnnotation ha:
                    bounds = ha.Bounds;
                    return bounds.Contains(pos);

                case TextAnnotation ta:
                    var ft = new FormattedText(ta.Content,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), ta.FontSize, Brushes.Black,
                        VisualTreeHelper.GetDpi(_activeCanvas).PixelsPerDip);
                    bounds = new Rect(ta.Position.X, ta.Position.Y, ft.Width + 8, ft.Height + 8);
                    return bounds.Contains(pos);

                case InkAnnotation ia when ia.Points.Count > 0:
                    bool near = ia.Points.Any(p =>
                        Math.Sqrt((p.X - pos.X) * (p.X - pos.X) + (p.Y - pos.Y) * (p.Y - pos.Y)) < 15);
                    if (near)
                    {
                        double minX = ia.Points.Min(p => p.X);
                        double minY = ia.Points.Min(p => p.Y);
                        double maxX = ia.Points.Max(p => p.X);
                        double maxY = ia.Points.Max(p => p.Y);
                        bounds = new Rect(minX, minY, Math.Max(maxX - minX, 4), Math.Max(maxY - minY, 4));
                        return true;
                    }
                    bounds = Rect.Empty;
                    return false;

                case TextEditAnnotation tea:
                    bounds = tea.OriginalBounds;
                    return bounds.Contains(pos);

                case SignatureAnnotation sa:
                    double sigW = sa.SourceWidth * sa.Scale;
                    double sigH = sa.SourceHeight * sa.Scale;
                    bounds = new Rect(sa.Position.X, sa.Position.Y, sigW, sigH);
                    return bounds.Contains(pos);

                case ImageAnnotation ia:
                    double iaW = ia.SourceWidth * ia.Scale;
                    double iaH = ia.SourceHeight * ia.Scale;
                    bounds = new Rect(ia.Position.X, ia.Position.Y, iaW, iaH);
                    return bounds.Contains(pos);

                default:
                    bounds = Rect.Empty;
                    return false;
            }
        }

        // Resolve the active theme's "SelectionAccent" color: a per-theme color picked to stay
        // readable on the white PDF page (Accent is white in several themes, and AccentBorder is a
        // pale cream that washes out on white). Falls back to brand green.
        private Color AccentColor()
            => TryFindResource("SelectionAccent") is SolidColorBrush b ? b.Color : Color.FromRgb(30, 165, 76);
        private SolidColorBrush AccentBrush(byte alpha = 255)
        {
            var c = AccentColor();
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        // Recolor any live selection / crop visuals to the current theme's SelectionAccent.
        // Their brushes are plain (not resource references), so a theme swap won't update them
        // until reselected unless we repaint them here.
        private void RefreshSelectionAccent()
        {
            if (_selectionBorder is not null)
            {
                _selectionBorder.BorderBrush = AccentBrush();
                _selectionBorder.Background  = AccentBrush(40);
            }
            foreach (var hd in _resizeHandles)
                hd.Fill = AccentBrush();
            if (_cropPreviewRect is not null)
                _cropPreviewRect.Fill = AccentBrush(55);
        }

        // Positions the four corner handles around an annotation's bounds (top-left x,y and size w,h).
        private void LayoutResizeHandles(double x, double y, double w, double h)
        {
            foreach (var hd in _resizeHandles)
            {
                double hs = hd.Width;
                (double cx, double cy) = (hd.Tag as string) switch
                {
                    "NW" => (x,     y),
                    "NE" => (x + w, y),
                    "SW" => (x,     y + h),
                    _    => (x + w, y + h)   // SE
                };
                Canvas.SetLeft(hd, cx - hs / 2);
                Canvas.SetTop(hd, cy - hs / 2);
            }
        }

        private void SelectAnnotation(PageAnnotation annot, Rect bounds)
        {
            _selectedAnnotation = annot;
            // Continuous-view overlays are scaled down by their LayoutTransform, which would
            // shrink the selection outline and resize handle to near-invisibility. Compensate
            // so they render at the same on-screen size as single-page view.
            double inv = 1.0;
            if (_activeCanvas.LayoutTransform is ScaleTransform _selScale && _selScale.ScaleX > 0.0001)
                inv = 1.0 / _selScale.ScaleX;
            _selectionBorder = new Border
            {
                BorderBrush = AccentBrush(),
                BorderThickness = new Thickness(2 * inv),
                Background = AccentBrush(40),
                Width = bounds.Width + 8,
                Height = bounds.Height + 8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(_selectionBorder, bounds.X - 4);
            Canvas.SetTop(_selectionBorder, bounds.Y - 4);
            _activeCanvas.Children.Add(_selectionBorder);

            // Add four corner resize handles for placed annotations (signature, image).
            if (annot is PlacedAnnotation)
            {
                double hSize = 14 * inv;
                _resizeHandles.Clear();
                foreach (string tag in new[] { "NW", "NE", "SE", "SW" })
                {
                    var hd = new Rectangle
                    {
                        Width = hSize, Height = hSize,
                        Fill = AccentBrush(),
                        Stroke = Brushes.White, StrokeThickness = 1 * inv,
                        Cursor = (tag is "NW" or "SE") ? Cursors.SizeNWSE : Cursors.SizeNESW,
                        IsHitTestVisible = true,
                        Tag = tag
                    };
                    _resizeHandles.Add(hd);
                    _activeCanvas.Children.Add(hd);
                }
                LayoutResizeHandles(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                string label = annot is SignatureAnnotation ? "Signature" : "Image";
                SetStatus($"{label} selected - drag any corner to resize, Delete to remove");
            }
            else
            {
                SetStatus($"Selected {annot.GetType().Name.Replace("Annotation", "").ToLower()} annotation - press Delete to remove");
            }
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// Returns true if <paramref name="element"/> is inside a form field overlay control
        /// (tagged with <see cref="FormOverlayTag"/>). Used to let WPF handle mouse events
        /// for TextBox, CheckBox, RadioButton, and ComboBox controls natively.
        /// </summary>
        private static bool IsFormFieldElement(DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.Tag as string == FormOverlayTag)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// Removes an overlay from the canvas that actually holds it. <c>_activeCanvas</c> moves
        /// as the user clicks between pages, so removing from it strands the visual on the page
        /// where it was drawn - it then stayed on screen until the app was restarted.
        /// </summary>
        private static void RemoveFromOwner(UIElement? element)
        {
            if (element is null) return;
            if (VisualTreeHelper.GetParent(element) is Canvas owner)
            {
                owner.Children.Remove(element);
                return;
            }
            if (LogicalTreeHelper.GetParent(element) is Canvas logicalOwner)
                logicalOwner.Children.Remove(element);
        }

        private void ClearSelection()
        {
            if (_selectionBorder is not null)
            {
                RemoveFromOwner(_selectionBorder);
                _selectionBorder = null;
            }
            foreach (var hd in _resizeHandles)
                RemoveFromOwner(hd);
            _resizeHandles.Clear();
            _isResizingSig = false;
            _resizeSigAnnot = null;
            _isDraggingAnnot = false;
            _dragAnnot = null;
            _selectedAnnotation = null;
        }

        private void DeleteSelected()
        {
            if (_selectedAnnotation is null) return;
            int pageIdx = _selectedAnnotation.PageIndex;
            // One undoable step that restores THIS annotation in its place - and marks the tab
            // dirty, which a delete used to skip, so closing without saving lost it silently.
            var deleted = _selectedAnnotation;
            ClearSelection();
            if (_annotations.TryGetValue(pageIdx, out var onPage) && onPage.Contains(deleted))
                ReplaceAnnotation(pageIdx, deleted, null);
            RenderAllAnnotations(pageIdx);
            SetStatus("Deleted selected annotation");
        }

        private void SelectAllText()
        {
            if (_currentFile is null) return;
            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) return;

            try
            {
                using var shown = OpenDisplayedPage(pageIdx);
                if (shown is null) return;
                _selectedText = WordsToText(shown.Page.GetWords());
                if (string.IsNullOrWhiteSpace(_selectedText))
                {
                    SetStatus("No text found on this page");
                    return;
                }
                Clipboard.SetText(_selectedText);
                // Visual feedback: highlight entire canvas
                ClearTextSelection();
                _selectRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(30, 74, 130, 255)),
                    Stroke = new SolidColorBrush(Color.FromArgb(80, 74, 130, 255)),
                    StrokeThickness = 1,
                    Width = _annotationCanvas.Width,
                    Height = _annotationCanvas.Height,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(_selectRect, 0);
                Canvas.SetTop(_selectRect, 0);
                _annotationCanvas.Children.Add(_selectRect);
                SetStatus($"Selected all text - copied to clipboard");
            }
            catch (Exception ex)
            {
                SetStatus($"Select all error: {ex.Message}");
            }
        }

        private void CopySelectedText()
        {
            if (!string.IsNullOrEmpty(_selectedText))
            {
                Clipboard.SetText(_selectedText);
                SetStatus($"Copied to clipboard");
            }
            else
            {
                SetStatus("No text selected - drag to select text");
            }
        }

        private void ClearTextSelection()
        {
            if (_selectRect is not null)
            {
                // Added to _annotationCanvas by SelectAllText but to the page canvas by a drag,
                // so it must be removed from whichever one owns it.
                RemoveFromOwner(_selectRect);
                _selectRect = null;
            }
            foreach (var mark in _textSelMarks) RemoveFromOwner(mark);
            _textSelMarks.Clear();
            _selectedText = null;
        }

        private void ExtractTextFromRegion(int pageIdx, Rect canvasBounds)
        {
            if (_currentFile is null || pageIdx < 0) return;
            if (!_renderDims.ContainsKey(pageIdx)) return;

            try
            {
                var (renderW, renderH) = _renderDims[pageIdx];

                // Same frame as the canvas (rotation, crop) - see OpenDisplayedPage.
                using var shown = OpenDisplayedPage(pageIdx);
                if (shown is null) return;
                var page = shown.Page;

                // Compare in canvas space: a word is selected when its centre is inside the drag.
                var hits = page.GetWords()
                    .Select(w => (Word: w, Rect: WordCanvasRect(w.BoundingBox, page.Width, page.Height, renderW, renderH)))
                    .Where(h => canvasBounds.Contains(new Point(h.Rect.X + h.Rect.Width / 2, h.Rect.Y + h.Rect.Height / 2)))
                    .ToList();

                if (hits.Count == 0)
                {
                    SetStatus("No text found in selection");
                    ClearTextSelection();
                    return;
                }

                _selectedText = WordsToText(hits.Select(h => h.Word));
                Clipboard.SetText(_selectedText);

                // Show WHAT was taken: the drag rectangle gives way to a highlight on each
                // selected word, and a toast says it went to the clipboard. The copy used to be
                // announced only in the status bar, so a drag looked like it did nothing.
                if (_selectRect is not null) { RemoveFromOwner(_selectRect); _selectRect = null; }
                var fill = new SolidColorBrush(Color.FromArgb(70, 74, 130, 255));
                foreach (var (_, r) in hits)
                {
                    var mark = new Rectangle
                    {
                        Fill = fill,
                        Width = Math.Max(1, r.Width + 2),
                        Height = Math.Max(1, r.Height + 2),
                        IsHitTestVisible = false,
                        Tag = TextSelMarkTag,
                    };
                    Canvas.SetLeft(mark, r.X - 1);
                    Canvas.SetTop(mark, r.Y - 1);
                    _activeCanvas.Children.Add(mark);
                    _textSelMarks.Add(mark);
                }
                string msg = string.Format(Loc("Str_Sel_Copied"), hits.Count);
                SetStatus(msg);
                ShowToast(msg);
            }
            catch (Exception ex)
            {
                SetStatus($"Text extraction error: {ex.Message}");
                ClearTextSelection();
            }
        }

        private const string TextSelMarkTag = "TextSelMark";
        /// <summary>The per-word highlights of the current drag selection (on its page canvas).</summary>
        private readonly List<Rectangle> _textSelMarks = [];

    }
}
