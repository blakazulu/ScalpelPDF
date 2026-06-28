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
        // App mode (View / Edit / Pages / Sign)
        // ============================================================

        private void ModeTab_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressModeEvents) return;
            if (sender is System.Windows.Controls.Primitives.ToggleButton tb && tb.Tag is string s
                && Enum.TryParse<AppMode>(s, out var m))
                SetMode(m);
        }

        private void SetMode(AppMode mode)
        {
            _mode = mode;
            _suppressModeEvents = true;
            ModeViewTab.IsChecked  = mode == AppMode.View;
            ModeEditTab.IsChecked  = mode == AppMode.Edit;
            ModePagesTab.IsChecked = mode == AppMode.Pages;
            ModeSignTab.IsChecked  = mode == AppMode.Sign;
            _suppressModeEvents = false;

            ModePanelView.Visibility  = mode == AppMode.View  ? Visibility.Visible : Visibility.Collapsed;
            ModePanelEdit.Visibility  = mode == AppMode.Edit  ? Visibility.Visible : Visibility.Collapsed;
            ModePanelPages.Visibility = mode == AppMode.Pages ? Visibility.Visible : Visibility.Collapsed;
            ModePanelSign.Visibility  = mode == AppMode.Sign  ? Visibility.Visible : Visibility.Collapsed;
        }

    }
}
