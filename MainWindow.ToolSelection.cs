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
        // Tool selection
        // ============================================================

        // Maps an editing tool to its mouse cursor. Shared by SetTool and by the
        // per-page overlay creation so freshly rendered tiles get the right cursor.
        private static Cursor CursorForTool(EditTool tool) => tool switch
        {
            EditTool.Text => Cursors.IBeam,
            EditTool.Highlight => Cursors.Cross,
            EditTool.Draw => Cursors.Pen,
            EditTool.Line => Cursors.Cross,
            EditTool.Signature => Cursors.Pen,
            EditTool.Image => Cursors.Hand,
            EditTool.Crop => Cursors.Cross,
            _ => Cursors.Arrow
        };

        private void SetTool(EditTool tool)
        {
            // Continuous view now supports annotation tools inline via per-page overlays.
            CommitActiveTextBox();
            ClearTextSelection();
            _currentTool = tool;

            var map = new (Button btn, EditTool t)[]
            {
                (_toolSelectBtn, EditTool.Select),
                (_toolTextBtn, EditTool.Text),
                (_toolHighlightBtn, EditTool.Highlight),
                (_toolDrawBtn, EditTool.Draw),
                (_toolLineBtn, EditTool.Line),
                (_toolSignatureBtn, EditTool.Signature),
                (_toolImageBtn, EditTool.Image),
                (_toolCropBtn, EditTool.Crop)
            };
            foreach (var (btn, t) in map)
            {
                if (t == tool)
                {
                    btn.SetResourceReference(Control.BackgroundProperty, "AccentDim");
                    btn.SetResourceReference(Control.ForegroundProperty, "Accent");
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
                }
            }

            // Apply the tool cursor to every page surface, not just the primary page.
            // In Grid / Two-Page / Continuous modes the secondary tiles are separate
            // overlay canvases tracked in _continuousCanvases; without this they keep
            // the default arrow cursor while only page 1 (_annotationCanvas) updates.
            var toolCursor = CursorForTool(tool);
            _annotationCanvas.Cursor = toolCursor;
            foreach (var overlay in _continuousCanvases.Values)
                overlay.Cursor = toolCursor;

            // Show/hide draw settings bar
            if (tool == EditTool.Draw || tool == EditTool.Highlight || tool == EditTool.Line)
                ShowDrawSettings(tool);
            else
                HideDrawSettings();

            // Show/hide text tool settings bar
            if (tool == EditTool.Text)
                ShowTextSettings();
            else
                HideTextSettings();

            // Hide signature popup when switching away
            if (tool != EditTool.Signature)
            {
                HideSignaturePopup();
                _pendingSignature = null;
            }

            // Dismiss crop confirm bar when switching away from Crop
            if (tool != EditTool.Crop)
                HideCropConfirmBar();
        }

        private void SidebarToggle_Click(object sender, RoutedEventArgs e)
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            if (_sidebarCollapsed)
            {
                // Save current width before collapsing so expand restores it.
                if (_sidebarCol.ActualWidth > 24)
                {
                    if (_sidebarShowingOutlines)
                        _savedOutlinesWidth = Math.Min(_sidebarCol.ActualWidth, SidebarMaxOutlines);
                    else
                        _savedPagesWidth = Math.Min(_sidebarCol.ActualWidth, SidebarMaxPages);
                }
                _sidebarBorder.Visibility = Visibility.Collapsed;
                _sidebarCol.Width = new GridLength(24);
                _sidebarCol.MinWidth = 24;
                _sidebarToggleBtn.Content = (string)FindResource("Ico_ChevronRight"); // expand right (Tabler)
                _sidebarToggleBtn.ToolTip = Loc("Str_TT_ExpandSidebar");
            }
            else
            {
                _sidebarBorder.Visibility = Visibility.Visible;
                double restore = _sidebarShowingOutlines ? _savedOutlinesWidth : _savedPagesWidth;
                _sidebarCol.Width = new GridLength(restore);
                _sidebarCol.MinWidth = 24;
                _sidebarToggleBtn.Content = (string)FindResource("Ico_ChevronLeft"); // collapse left (Tabler)
                _sidebarToggleBtn.ToolTip = Loc("Str_TT_CollapseSidebar");
            }
            if (PageList.SelectedIndex >= 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    () => RefreshPageView(PageList.SelectedIndex));
        }

    }
}
