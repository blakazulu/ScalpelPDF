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
        // Dirty / unsaved-change tracking
        // ============================================================

        private void MarkDirty(bool dirty = true)
        {
            bool changed = _isDirty != dirty;
            _isDirty = dirty;
            if (_saveAsBtnRef != null)
            {
                _saveAsBtnRef.Foreground = dirty
                    ? new SolidColorBrush(Color.FromRgb(0xff, 0xa5, 0x00)) // orange = unsaved
                    : (SolidColorBrush)FindResource("Accent");
            }
            // The active tab's unsaved marker follows the document live.
            if (changed) RefreshTabStrip();
        }

        // ============================================================
        // Close file (Ctrl+W) - closes the active tab (MainWindow.Tabs.cs:CloseSession)
        // ============================================================

        private void CloseFile()
        {
            if (_doc is null) return;
            CloseSession(_s);
        }

        /// <summary>
        /// The window with no document: drop zone and recent list, every canvas, label and
        /// sidebar cleared. UI only - the session model is owned by the tab list.
        /// </summary>
        private void ShowEmptyState()
        {
            _bindingSession = false;
            _activeTextBox = null;   // cancel any in-progress typewriter edit before canvas clear
            try { _thumbCts?.Cancel(); } catch { }
            PageList.ItemsSource = null;
            if (FindName("PageImage") is System.Windows.Controls.Image img) img.Source = null;
            _annotationCanvas.Children.Clear();
            FileNameLabel.Text = "";
            DropZone.Visibility = Visibility.Visible;
            PopulateRecentList();
            PagePreviewPanel.Visibility = Visibility.Collapsed;
            CloseSearchBar();
            HideDrawSettings();
            HideTextSettings();
            HideSignaturePopup();
            SetTool(EditTool.Select);
            if (_closeFileBtnRef != null) _closeFileBtnRef.IsEnabled = false;
            _pageJumpBox.IsEnabled = false;
            _continuousRenderCts?.Cancel();
            _secondaryRenderCts?.Cancel();
            ClearSecondaryPages();
            _continuousPanel.Children.Clear();
            _continuousTops.Clear();
            _pageJumpBox.Text = "";
            _pageTotalLabel.Text = "/ –";
            OutlineTree.Items.Clear();
            SidebarOutlinesTab.IsEnabled = false;
            if (_sidebarShowingOutlines) SwitchSidebarToPagesTab();
            MarkDirty(false);
            RefreshTabStrip();
            SetStatus("Ready");
        }

        private void CloseFile_Click(object sender, RoutedEventArgs e) => CloseFile();

    }
}
