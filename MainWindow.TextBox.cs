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
        // Text box handling
        // ============================================================

        /// <summary>Clears the cached printed-blank geometry (the page raster changed under it).</summary>
        private void InvalidateBlankCache() => _blankCache.Clear();

        /// <summary>
        /// The printed blank under <paramref name="pos"/>, in canvas coordinates, or null.
        /// Lets a click on a dotted line drop the text box onto the line instead of near it.
        /// </summary>
        private Rect? FindPrintedBlank(Point pos, int pageIdx)
        {
            try
            {
                if (_currentFile is null || _doc is null) return null;
                if (!_renderDims.TryGetValue(pageIdx, out var dims) || dims.w <= 0 || dims.h <= 0)
                    return null;

                if (!_blankCache.TryGetValue(pageIdx, out var candidates))
                {
                    candidates = [];
                    var page = _doc.Pages[pageIdx];
                    double pw = page.Width.Point, ph = page.Height.Point;
                    _pageRotations.TryGetValue(pageIdx, out int rot);

                    using (var pig = UglyToad.PdfPig.PdfDocument.Open(_currentFile))
                    {
                        if (pageIdx < pig.NumberOfPages)
                        {
                            foreach (var w in pig.GetPage(pageIdx + 1).GetWords())
                            {
                                if (!Scalpel.Services.TextEntryPlaceholder.IsPlaceholder(w.Text)) continue;
                                var bb = w.BoundingBox;
                                var r = Scalpel.Services.PageSpaceMap.ToCanvas(
                                            bb.Left, bb.Bottom, bb.Right, bb.Top,
                                            pw, ph, dims.w, dims.h, rot);
                                candidates.Add(new Scalpel.Services.TextEntryPlaceholder.Candidate(
                                    w.Text, new Rect(r.X, r.Y, r.W, r.H)));
                            }
                        }
                    }
                    _blankCache[pageIdx] = candidates;
                }

                return candidates.Count == 0
                    ? null
                    : Scalpel.Services.TextEntryPlaceholder.FindNearest(candidates, pos);
            }
            catch { return null; }   // a convenience must never block placing a text box
        }

        private void PlaceTextBox(Point pos, int pageIdx)
        {
            // On a flattened form the user aims at the dotted line, not at a pixel. Snap the box
            // onto the blank they clicked so the typed text sits on the rule instead of beside it.
            Rect? blank = FindPrintedBlank(pos, pageIdx);
            // _textFontSize is a point size; convert to the page's canvas (render-dim) units so
            // it renders and exports as real points. DrawAnnotationsOnDocument multiplies by
            // sy = page.Height.Point / renderH, so dividing by sy here makes "14" export as 14pt.
            double fontCanvas = _textFontSize;
            if (_doc is not null && _renderDims.TryGetValue(pageIdx, out var rdims) && rdims.h > 0)
            {
                double sy = _doc.Pages[pageIdx].Height.Point / rdims.h;
                if (sy > 0) fontCanvas = _textFontSize / sy;
            }
            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
                Foreground = new SolidColorBrush(_textColor),
                BorderBrush = (SolidColorBrush)FindResource("Accent"), SelectionBrush = AccentBrush(),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI, Noto Sans Hebrew, Noto Sans Arabic"),
                FontSize = fontCanvas,
                MinWidth = 120,
                MinHeight = 24,
                Padding = new Thickness(2),
                AcceptsReturn = true,
                Tag = pageIdx
            };
            if (blank is Rect b)
            {
                // Match the blank's width and sit the box on the rule, growing upward when the
                // box is taller than the printed line so the text lands on it rather than below.
                double w = Math.Max(40, b.Width);
                tb.MinWidth = w;
                tb.Width = w;
                Canvas.SetLeft(tb, b.Left);
                Canvas.SetTop(tb, b.Top - Math.Max(0, tb.MinHeight - b.Height));
            }
            else
            {
                Canvas.SetLeft(tb, pos.X);
                Canvas.SetTop(tb, pos.Y);
            }
            _activeCanvas.Children.Add(tb);
            _activeTextBox = tb;
            tb.TextChanged += (s, e) =>
            {
                tb.FlowDirection = Scalpel.Services.BidiReorder.ContainsRtl(tb.Text)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            };
            tb.KeyDown += TextBox_KeyDown;
            // Defer focus until the TextBox is actually rendered
            tb.Loaded += (s, e) =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.LostFocus += TextBox_LostFocus;
            };
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_activeTextBox is not null)
                {
                    _activeCanvas.Children.Remove(_activeTextBox);
                    _activeTextBox = null;
                }
                if (_reeditOriginal is not null)
                {
                    int rp = _reeditOriginal.PageIndex;
                    if (!_annotations.TryGetValue(rp, out var rlist)) { rlist = []; _annotations[rp] = rlist; }
                    rlist.Add(_reeditOriginal);
                    _reeditOriginal = null;
                    RenderAllAnnotations(rp);
                }
                if (_currentTool != EditTool.Text) HideTextSettings();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                CommitActiveTextBox();
                e.Handled = true;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Only commit if the TextBox actually has content
            if (_activeTextBox is not null && !string.IsNullOrWhiteSpace(_activeTextBox.Text))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Keep the edit box open if focus moved into the size/color bar so the
                    // user can restyle (the Size ComboBox takes focus; color swatches do not).
                    if (_textSettingsBar is not null && Keyboard.FocusedElement is DependencyObject fe
                        && IsDescendantOf(fe, _textSettingsBar))
                        return;
                    CommitActiveTextBox();
                }),
                System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void CommitActiveTextBox()
        {
            if (_activeTextBox is null) return;
            // If it's an inline text edit, use the dedicated commit path
            if (_activeTextBox.Tag is TextEditContext)
            {
                CommitTextEdit();
                return;
            }
            var tb = _activeTextBox;
            _activeTextBox = null;
            _reeditOriginal = null;   // committing replaces any annotation being re-edited

            string content = tb.Text.Trim();
            int pageIdx = tb.Tag is int idx ? idx : PageList.SelectedIndex;
            double x = Canvas.GetLeft(tb);
            double y = Canvas.GetTop(tb);

            _activeCanvas.Children.Remove(tb);

            if (!string.IsNullOrEmpty(content))
            {
                var ta = new TextAnnotation
                {
                    PageIndex = pageIdx,
                    Position = new Point(x, y),
                    Content = content,
                    FontSize = tb.FontSize,
                    // Carry the styling the user applied with Ctrl+B / I / U while typing.
                    Bold = tb.FontWeight == FontWeights.Bold,
                    Italic = tb.FontStyle == FontStyles.Italic,
                    Underline = tb.TextDecorations is not null && tb.TextDecorations.Count > 0,
                };
                ta.SetColor(tb.Foreground is SolidColorBrush scb ? scb.Color : Colors.Black);
                AddAnnotation(ta);
                RenderTextAnnotation(ta);
            }
            if (_currentTool != EditTool.Text) HideTextSettings();
        }

    }
}
