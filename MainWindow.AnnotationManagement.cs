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
        // Annotation management
        // ============================================================

        private void AddAnnotation(PageAnnotation annotation)
        {
            if (!_annotations.ContainsKey(annotation.PageIndex))
                _annotations[annotation.PageIndex] = [];
            _annotations[annotation.PageIndex].Add(annotation);
            PushUndoEntry(new UndoEntry(UndoKind.Annotation, annotation.PageIndex, WasDirty: _isDirty));
            // Any new edit invalidates the redo chain, the way every editor behaves.
            _redoStack.Clear();
            MarkDirty();
        }

        /// <summary>
        /// Saves the current in-memory document bytes onto the undo stack so that
        /// document-level operations (crop, delete page, merge, reorder) can be undone.
        /// Must be called BEFORE modifying _doc.
        /// </summary>
        private void PushDocUndo()
        {
            if (_doc is null) return;
            using var ms = new System.IO.MemoryStream();
            _doc.Save(ms);
            PushUndoEntry(new UndoEntry(UndoKind.Document, DocBytes: ms.ToArray(), WasDirty: _isDirty));
            _redoStack.Clear();
            TrimUndoAcrossTabs();
        }

        /// <summary>
        /// Measures the on-screen box of a text annotation. Scalpel's model stores only the anchor
        /// point - the box is laid out at render time - so a rotation remap needs this to carry the
        /// box through the turn instead of treating the text as a zero-size point.
        /// </summary>
        private static Size MeasureTextAnnotation(TextAnnotation ta)
        {
            try
            {
                var tb = new TextBlock
                {
                    Text = ta.Content,
                    FontSize = ta.FontSize,
                    // Bold and italic change the measured width, so the box must be measured
                    // in the style it will actually be drawn in.
                    FontWeight = ta.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = ta.Italic ? FontStyles.Italic : FontStyles.Normal,
                    Padding = new Thickness(2)
                };
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var d = tb.DesiredSize;
                // net48 has no double.IsFinite.
                bool finite = !double.IsNaN(d.Width) && !double.IsInfinity(d.Width)
                              && !double.IsNaN(d.Height) && !double.IsInfinity(d.Height);
                return finite ? d : new Size(0, 0);
            }
            catch { return new Size(0, 0); }
        }

        private void RenderTextAnnotation(TextAnnotation ta)
        {
            var tb = new TextBlock
            {
                Text = ta.Content,
                Foreground = new SolidColorBrush(ta.GetColor()),
                FontFamily = (FontFamily)FindResource("FontUI"),
                FontSize = ta.FontSize,
                FontWeight = ta.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = ta.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = ta.Underline ? TextDecorations.Underline : null,
                Padding = new Thickness(2)
            };
            if (Scalpel.Services.BidiReorder.ContainsRtl(ta.Content))
            {
                tb.FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic");
                tb.FlowDirection = FlowDirection.RightToLeft;
            }
            Canvas.SetLeft(tb, ta.Position.X);
            Canvas.SetTop(tb, ta.Position.Y);
            _activeCanvas.Children.Add(tb);
        }

        private void RenderAllAnnotations(int pageIndex)
        {
            // Resolve this page's annotation surface from the unified per-page overlay map, which
            // every multi-page view populates; fall back to the single-page canvas. View-mode
            // independent on purpose so the tools behave identically in all four modes.
            _activeCanvas = _continuousCanvases.TryGetValue(pageIndex, out var pageCanvas)
                ? pageCanvas : _annotationCanvas;
            _activeCanvas.Children.Clear();

            if (_annotations.TryGetValue(pageIndex, out var annotList))
            foreach (var annot in annotList)
            {
                switch (annot)
                {
                    case TextAnnotation ta:
                        RenderTextAnnotation(ta);
                        break;
                    case HighlightAnnotation ha:
                        var rect = new Rectangle
                        {
                            Fill = new SolidColorBrush(ha.GetColor()),
                            Width = ha.Bounds.Width,
                            Height = ha.Bounds.Height
                        };
                        Canvas.SetLeft(rect, ha.Bounds.X);
                        Canvas.SetTop(rect, ha.Bounds.Y);
                        _activeCanvas.Children.Add(rect);
                        break;
                    case InkAnnotation ia:
                        if (ia.Points.Count < 2) continue;
                        var poly = new Polyline
                        {
                            Stroke = new SolidColorBrush(ia.GetColor()),
                            StrokeThickness = ia.StrokeWidth,
                            StrokeLineJoin = PenLineJoin.Round,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round
                        };
                        foreach (var pt in ia.Points) poly.Points.Add(pt);
                        _activeCanvas.Children.Add(poly);
                        break;
                    case TextEditAnnotation tea:
                        // White-out original text
                        var wo = new Rectangle
                        {
                            Fill = Brushes.White,
                            Width = tea.OriginalBounds.Width + 4,
                            Height = tea.OriginalBounds.Height + 4,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(wo, tea.OriginalBounds.X - 2);
                        Canvas.SetTop(wo, tea.OriginalBounds.Y - 2);
                        _activeCanvas.Children.Add(wo);
                        // Draw replacement text
                        var etb = new TextBlock
                        {
                            Text = tea.NewContent,
                            Foreground = Brushes.Black,
                            FontFamily = new FontFamily(tea.FontName),
                            FontSize = tea.FontSize,
                            FontWeight = ToWeight(tea.IsBold),
                            FontStyle = ToStyle(tea.IsItalic),
                            Padding = new Thickness(0)
                        };
                        Canvas.SetLeft(etb, tea.Position.X);
                        Canvas.SetTop(etb, tea.Position.Y);
                        _activeCanvas.Children.Add(etb);
                        break;

                    case SignatureAnnotation sa:
                        if (sa.ImageData is not null)
                        {
                            // Image-based signature
                            try
                            {
                                var imgBytes = Convert.FromBase64String(sa.ImageData);
                                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                                bmp.BeginInit();
                                bmp.StreamSource = new System.IO.MemoryStream(imgBytes);
                                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                var imgCtrl = new System.Windows.Controls.Image
                                {
                                    Source = bmp,
                                    Width = sa.SourceWidth * sa.Scale,
                                    Height = sa.SourceHeight * sa.Scale,
                                    Stretch = System.Windows.Media.Stretch.Uniform,
                                    IsHitTestVisible = false
                                };
                                Canvas.SetLeft(imgCtrl, sa.Position.X);
                                Canvas.SetTop(imgCtrl, sa.Position.Y);
                                _activeCanvas.Children.Add(imgCtrl);
                            }
                            catch { /* skip broken image */ }
                        }
                        else
                        {
                            foreach (var stroke in sa.Strokes)
                            {
                                if (stroke.Count < 2) continue;
                                var sigPoly = new Polyline
                                {
                                    Stroke = Brushes.Black,
                                    StrokeThickness = 2 * sa.Scale,
                                    StrokeLineJoin = PenLineJoin.Round,
                                    StrokeStartLineCap = PenLineCap.Round,
                                    StrokeEndLineCap = PenLineCap.Round
                                };
                                foreach (var pt in stroke)
                                    sigPoly.Points.Add(new Point(
                                        sa.Position.X + pt.X * sa.Scale,
                                        sa.Position.Y + pt.Y * sa.Scale));
                                _activeCanvas.Children.Add(sigPoly);
                            }
                        }
                        break;

                    case ImageAnnotation ia:
                        try
                        {
                            var iaBytes = Convert.FromBase64String(ia.ImageData);
                            var iaBmp = new System.Windows.Media.Imaging.BitmapImage();
                            iaBmp.BeginInit();
                            iaBmp.StreamSource = new System.IO.MemoryStream(iaBytes);
                            iaBmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            iaBmp.EndInit();
                            var iaCtrl = new System.Windows.Controls.Image
                            {
                                Source = iaBmp,
                                Width = ia.SourceWidth * ia.Scale,
                                Height = ia.SourceHeight * ia.Scale,
                                Stretch = System.Windows.Media.Stretch.Uniform,
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(iaCtrl, ia.Position.X);
                            Canvas.SetTop(iaCtrl, ia.Position.Y);
                            _activeCanvas.Children.Add(iaCtrl);
                        }
                        catch { /* skip broken image */ }
                        break;
                }
            }

            // Re-add form field overlays — RenderAllAnnotations clears the canvas so they must be restored.
            if (_renderDims.TryGetValue(pageIndex, out var dims))
                RenderFormFields(pageIndex, dims.w, dims.h);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0)
            {
                SetStatus(Loc("Str_St_NothingToUndo"));
                return;
            }

            var entry = _undoStack.Pop();

            if (entry.Kind == UndoKind.Annotation)
            {
                int pageIdx = entry.PageIdx;
                PageAnnotation? removed = null;
                if (_annotations.ContainsKey(pageIdx) && _annotations[pageIdx].Count > 0)
                {
                    removed = _annotations[pageIdx][^1];
                    _annotations[pageIdx].RemoveAt(_annotations[pageIdx].Count - 1);
                }
                PushRedoEntry(entry with { Annotation = removed });
                ClearSelection();
                RenderAllAnnotations(pageIdx);
                MarkDirty(entry.WasDirty);
                SetStatus(Loc("Str_St_Undid"));
            }
            else // Document snapshot
            {
                if (entry.DocBytes is null) return;
                // Snapshot the current state first so Redo can return to it.
                try
                {
                    if (_doc is not null)
                    {
                        using var redoMs = new System.IO.MemoryStream();
                        _doc.Save(redoMs);
                        PushRedoEntry(new UndoEntry(UndoKind.Document, DocBytes: redoMs.ToArray(),
                                                      WasDirty: _isDirty));
                        TrimUndoAcrossTabs();
                    }
                }
                catch { /* a missing redo step must never block the undo */ }
                int selectedIdx = PageList.SelectedIndex;
                var tempPath = App.MakeTempFile("undo", _s.Id);
                System.IO.File.WriteAllBytes(tempPath, entry.DocBytes);
                _doc?.Close();
                // PdfSharpCore can write a snapshot whose xref offset points at the xref table,
                // producing "Unexpected token 'xref'" on reopen. Repair via Import (preserves
                // rotations) then PDFium, mirroring the save/reload path, instead of crashing.
                try
                {
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                catch (Exception undoOpenEx) when (IsXRefException(undoOpenEx))
                {
                    var fixedPath = App.MakeTempFile("undofixed", _s.Id);
                    if (!TryImportRepairToPath(tempPath, fixedPath)
                        && !TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                        throw;
                    tempPath = fixedPath;
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                _currentFile = tempPath;
                _annotations.Clear();
                _renderDims.Clear();
                ClearSelection();
                MarkDirty(entry.WasDirty);
                RefreshPageList();
                if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                    PageList.SelectedIndex = selectedIdx;
                else if (PageList.Items.Count > 0)
                    PageList.SelectedIndex = 0;
                // Re-render the current view so the main page(s) reflect the restored document.
                // RefreshPageList only updates the sidebar, and re-selecting the same page does not
                // fire SelectionChanged, so grid/two-page tiles would otherwise stay stale.
                int reIdx = PageList.SelectedIndex;
                // A tab switch before these run makes them another document's work: bail.
                int reIdxGen = _sessionGeneration;
                if (_viewMode == ViewMode.Continuous)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        (Action)(() => { if (!IsStale(reIdxGen)) SetupContinuousView(reIdx); }));
                else
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                    {
                        if (IsStale(reIdxGen)) return;
                        RenderPage(_viewMode == ViewMode.Grid ? 0 : reIdx);
                        ReapplyGridOrFit();
                    }));
                SetStatus(Loc("Str_St_UndidDoc"));
            }
        }

        /// <summary>
        /// Ctrl+Y / Ctrl+Shift+Z. Re-applies what Undo took away. The chain is cleared by any
        /// new edit, so redo can only ever walk back up the path the user just undid.
        /// </summary>
        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count == 0)
            {
                SetStatus(Loc("Str_St_NothingToRedo"));
                return;
            }

            var entry = _redoStack.Pop();

            if (entry.Kind == UndoKind.Annotation)
            {
                if (entry.Annotation is null) { SetStatus(Loc("Str_St_NothingToRedo")); return; }
                int pageIdx = entry.PageIdx;
                if (!_annotations.ContainsKey(pageIdx)) _annotations[pageIdx] = [];
                _annotations[pageIdx].Add(entry.Annotation);
                // Push straight onto the undo stack rather than through AddAnnotation, which
                // would clear the redo chain we are walking.
                PushUndoEntry(new UndoEntry(UndoKind.Annotation, pageIdx, WasDirty: _isDirty));
                ClearSelection();
                RenderAllAnnotations(pageIdx);
                MarkDirty();
                SetStatus(Loc("Str_St_Redid"));
                return;
            }

            if (entry.DocBytes is null) return;
            try
            {
                int selectedIdx = PageList.SelectedIndex;
                using (var undoMs = new System.IO.MemoryStream())
                {
                    _doc?.Save(undoMs);
                    PushUndoEntry(new UndoEntry(UndoKind.Document, DocBytes: undoMs.ToArray(),
                                                  WasDirty: _isDirty));
                    TrimUndoAcrossTabs();
                }

                var tempPath = App.MakeTempFile("redo", _s.Id);
                System.IO.File.WriteAllBytes(tempPath, entry.DocBytes);
                _doc?.Close();
                try
                {
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                catch (Exception redoOpenEx) when (IsXRefException(redoOpenEx))
                {
                    var fixedPath = App.MakeTempFile("redofixed", _s.Id);
                    if (!TryImportRepairToPath(tempPath, fixedPath)
                        && !TryPdfiumSaveWithZeroRotations(tempPath, fixedPath))
                        throw;
                    tempPath = fixedPath;
                    _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                }
                _currentFile = tempPath;
                _annotations.Clear();
                _renderDims.Clear();
                ClearSelection();
                MarkDirty();
                RefreshPageList();
                if (selectedIdx >= 0 && selectedIdx < PageList.Items.Count)
                    PageList.SelectedIndex = selectedIdx;
                else if (PageList.Items.Count > 0)
                    PageList.SelectedIndex = 0;

                int reIdx2 = PageList.SelectedIndex;
                // A tab switch before these run makes them another document's work: bail.
                int reIdx2Gen = _sessionGeneration;
                if (_viewMode == ViewMode.Continuous)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        (Action)(() => { if (!IsStale(reIdx2Gen)) SetupContinuousView(reIdx2); }));
                else
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
                    {
                        if (IsStale(reIdx2Gen)) return;
                        RenderPage(_viewMode == ViewMode.Grid ? 0 : reIdx2);
                        ReapplyGridOrFit();
                    }));
                SetStatus(Loc("Str_St_RedidDoc"));
            }
            catch (Exception ex)
            {
                Scalpel.Services.Logger.Error("Edit", "redo.fail", ex.Message, ex);
                SetStatus(Loc("Str_St_NothingToRedo"));
            }
        }

        private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
        {
            int pageIdx = PageList.SelectedIndex;
            if (pageIdx < 0) return;
            if (_annotations.ContainsKey(pageIdx) && _annotations[pageIdx].Count > 0)
            {
                _annotations[pageIdx].Clear();
                MarkDirty();
            }
            ClearSelection();
            _annotationCanvas.Children.Clear();
            SetStatus("Cleared annotations on this page");
        }

    }
}
