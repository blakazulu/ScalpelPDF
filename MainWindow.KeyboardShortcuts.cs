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

        /// <summary>
        /// Labels the zoom shortcut with the characters this keyboard actually types. Advertising
        /// "Ctrl+=" on a layout where "=" needs Shift is simply untrue, so the label follows the
        /// active layout (German prints Ctrl++, US keeps Ctrl+=).
        /// </summary>
        private void SyncZoomShortcutLabel()
        {
            try
            {
                if (ZoomShortcutLabel is null) return;
                ZoomShortcutLabel.Text =
                    $"Ctrl+{Scalpel.Services.KeyLayout.ZoomInChar()} / Ctrl+{Scalpel.Services.KeyLayout.ZoomOutChar()}";
            }
            catch { }   // a label is never worth an exception
        }
        /// <summary>1..9 for the digit keys (top row or number pad), else 0.</summary>
        private static int TabNumberForKey(Key key) => key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0
        };

        // ============================================================
        // Keyboard shortcuts
        // ============================================================

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // While a TextBox has focus (typewriter tool or a fillable form field), typing and the
            // standard text-editing chords stay inside the field - but application shortcuts such
            // as Ctrl+S, Ctrl+P, Ctrl+F, Ctrl+Tab and the function keys must keep working, or the
            // whole app goes dead the moment a form field is clicked.
            bool inTextBox = e.OriginalSource is TextBox
                             || (_activeTextBox is not null && _activeTextBox.IsFocused);
            if (inTextBox &&
                Scalpel.Services.EditableTextShortcutPolicy.KeepInTextBox(e.Key, Keyboard.Modifiers, e.SystemKey))
                return;

            // Ctrl+B / I / U style the text box being typed in. Handled before the generic
            // text-box guard would otherwise pass them through as plain typing.
            if (_activeTextBox is not null && Keyboard.Modifiers == ModifierKeys.Control
                && e.Key is Key.B or Key.I or Key.U)
            {
                var box = _activeTextBox;
                switch (e.Key)
                {
                    case Key.B:
                        box.FontWeight = box.FontWeight == FontWeights.Bold
                            ? FontWeights.Normal : FontWeights.Bold;
                        break;
                    case Key.I:
                        box.FontStyle = box.FontStyle == FontStyles.Italic
                            ? FontStyles.Normal : FontStyles.Italic;
                        break;
                    default:
                        box.TextDecorations = box.TextDecorations is { Count: > 0 }
                            ? null : TextDecorations.Underline;
                        break;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CopySelectedText();
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SelectAllText();
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleSearchBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _currentTool == EditTool.Crop && _cropConfirmBar is not null)
            {
                ApplyCrop([PageList.SelectedIndex]);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _currentTool == EditTool.Crop && _cropConfirmBar is not null)
            {
                HideCropConfirmBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && OcrProgressOverlay.Visibility == Visibility.Visible)
            {
                _ocrCts?.Cancel();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ShortcutOverlay.Visibility == Visibility.Visible)
            {
                ShortcutOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _searchBar is not null && _searchBar.Visibility == Visibility.Visible)
            {
                CloseSearchBar();
                e.Handled = true;
            }
            // Matched by the character the key TYPES, not its position: on a German keyboard "?"
            // is Shift+ss, so a virtual-key test for OemQuestion never fires and the exact
            // modifier comparison fails again because Shift is down. See Services/KeyLayout.cs.
            else if (Scalpel.Services.KeyLayout.IsCtrlChar(e.Key, '?'))
            {
                SyncZoomShortcutLabel();
                ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true;
            }
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Print_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _selectedAnnotation is not null)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                Redo_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Undo_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Redo_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                SaveAs_Click(this, e);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveInPlace();
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                CloseOtherTabs();
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CloseSession(_s);
                e.Handled = true;
            }
            else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Open_Click(this, e);
                e.Handled = true;
            }
            else if ((e.Key == Key.Tab || e.Key == Key.PageDown) && Keyboard.Modifiers == ModifierKeys.Control && _tabs.Count > 1)
            {
                CycleTab(true);
                e.Handled = true;
            }
            else if (((e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                      || (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)) && _tabs.Count > 1)
            {
                CycleTab(false);
                e.Handled = true;
            }
            else if ((e.Key is Key.N or Key.T) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                NewDocument();   // Ctrl+N and Ctrl+T both: a blank document in a new tab
                e.Handled = true;
            }
            else if ((e.Key == Key.Left || e.Key == Key.Up) && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && PageList.SelectedIndex > 0)
                {
                    PageList.SelectedIndex--;
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.Right || e.Key == Key.Down) && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && PageList.SelectedIndex < _doc.PageCount - 1)
                {
                    PageList.SelectedIndex++;
                    e.Handled = true;
                }
            }
            // Ctrl+1..Ctrl+8 go to that tab, Ctrl+9 to the last one (top-row or numpad digits).
            // This is matched by the KEY, before the layout-aware zoom checks below, on purpose:
            // on AZERTY the digit keys type "-" (6) and "_" (8), and on Czech/Slovak the 1 key
            // types "+", so a character match would turn Ctrl+6 / Ctrl+8 / Ctrl+1 into zoom.
            // The user decided Ctrl+digit belongs to the tabs (ruling R17); zoom stays reachable
            // on those layouts via the dedicated +/- keys, the numpad +/-, Ctrl+wheel and Ctrl+0.
            // Only Ctrl alone: Ctrl+Shift+digit is left to the zoom presets and character matches.
            else if (Keyboard.Modifiers == ModifierKeys.Control && TabNumberForKey(e.Key) is int tabNo and > 0)
            {
                var t = _tabs.ByNumber(tabNo);
                // A deferred tab (startup-restored, not yet loaded) is a real tab too - Ctrl+digit
                // materializes it like clicking its chip would.
                if (t is not null && (t.Doc is not null || t.DeferredPath is not null)) SwitchTo(t);
                e.Handled = true;
            }
            else if (Scalpel.Services.KeyLayout.IsCtrlChar(e.Key, '+', '=')
                     || (e.Key == Key.Add && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (_viewMode == ViewMode.Grid) GridZoomStep(false); else SetZoom(_zoomLevel + ZoomStep);
                e.Handled = true;
            }
            else if (Scalpel.Services.KeyLayout.IsCtrlChar(e.Key, '-', '_')
                     || (e.Key == Key.Subtract && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (_viewMode == ViewMode.Grid) GridZoomStep(true); else SetZoom(_zoomLevel - ZoomStep);
                e.Handled = true;
            }
            // Zoom presets: Ctrl+0 actual size, Ctrl+Shift+2 fit width, Ctrl+Shift+3 fit page.
            // Plain Ctrl+1..9 belong to the tabs (above), as in every browser.
            else if ((e.Key == Key.D0 || e.Key == Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ZoomToActualSize();
                e.Handled = true;
            }
            else if ((e.Key == Key.D2 || e.Key == Key.NumPad2)
                     && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                FitToWidth();
                e.Handled = true;
            }
            else if ((e.Key == Key.D3 || e.Key == Key.NumPad3)
                     && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                FitToPage();
                e.Handled = true;
            }
            else if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && _doc.PageCount > 0) { PageList.SelectedIndex = 0; e.Handled = true; }
            }
            else if (e.Key == Key.End && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_doc is not null && _doc.PageCount > 0)
                {
                    PageList.SelectedIndex = _doc.PageCount - 1;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                ShowFileSizeStatus();
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                if (_searchBar is null || _searchBar.Visibility != Visibility.Visible) ToggleSearchBar();
                else if (Keyboard.Modifiers == ModifierKeys.Shift) SearchPrevResult();
                else SearchNextResult();
                e.Handled = true;
            }
            else if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
            {
                ShowDocumentInfo();
                e.Handled = true;
            }
            else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowDocumentInfo();
                e.Handled = true;
            }
            else if (e.Key == Key.F12)
            {
                ShowAboutOverlay();
                e.Handled = true;
            }
            else if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
            else if (e.Key == Key.F1)  { SyncZoomShortcutLabel(); ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; e.Handled = true; }
            else if (e.Key == Key.F5)  { SetViewMode(ViewMode.Single);     e.Handled = true; }
            else if (e.Key == Key.F6)  { SetViewMode(ViewMode.Continuous); e.Handled = true; }
            else if (e.Key == Key.F7)  { SetViewMode(ViewMode.TwoPage);    e.Handled = true; }
            else if (e.Key == Key.F8)  { SetViewMode(ViewMode.Grid);       e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.None &&
                     (e.Key == Key.V || e.Key == Key.T || e.Key == Key.H || e.Key == Key.D
                      || e.Key == Key.L || e.Key == Key.I || e.Key == Key.C || e.Key == Key.G))
            {
                SetMode(AppMode.Edit);
                SetTool(e.Key switch
                {
                    Key.V => EditTool.Select,
                    Key.T => EditTool.Text,
                    Key.H => EditTool.Highlight,
                    Key.D => EditTool.Draw,
                    Key.L => EditTool.Line,
                    Key.C => EditTool.Crop,
                    Key.G => EditTool.Signature,
                    _     => EditTool.Image,   // Key.I
                });
                e.Handled = true;
            }
            // Digits mirror the toolbar left to right, matching the convention upstream settled on.
            else if (Keyboard.Modifiers == ModifierKeys.None &&
                     e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D5 or Key.D6 or Key.D7 or Key.D8)
            {
                SetMode(AppMode.Edit);
                SetTool(e.Key switch
                {
                    Key.D1 => EditTool.Text,
                    Key.D2 => EditTool.Highlight,
                    Key.D3 => EditTool.Line,
                    Key.D5 => EditTool.Draw,
                    Key.D6 => EditTool.Image,
                    Key.D7 => EditTool.Signature,
                    _      => EditTool.Crop,   // Key.D8
                });
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_fullScreen) { ApplyFullScreen(false); e.Handled = true; return; }
                // Esc steps down rather than straight out: first it returns to the Select tool
                // (the convention every large viewer uses), and only a second Esc closes the app.
                if (_currentTool != EditTool.Select)
                {
                    SetMode(AppMode.Edit);
                    SetTool(EditTool.Select);
                    e.Handled = true;
                    return;
                }
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && !_spaceHeld)
            {
                _spaceHeld = true;
                PagePreviewPanel.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
            if (e.Key == Key.Space && _spaceHeld)
            {
                _spaceHeld = false;
                if (!_isPanning)
                    PagePreviewPanel.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

    }
}
