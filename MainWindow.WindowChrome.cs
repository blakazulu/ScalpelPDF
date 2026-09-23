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
        // Window chrome
        // ============================================================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeBtn_Click(sender, e);
                return;
            }
            // Delegate drag to Windows via WM_NCLBUTTONDOWN(HTCAPTION).
            // This gives native restore-from-maximized-and-drag behavior:
            // if the window is maximized, Windows restores it and follows the cursor
            // exactly as a native title bar would.
            e.Handled = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var (proceed, wantDesktop) = Scalpel.Services.InstallerUI.ShowInstallConfirm(alreadyInstalled: false);
            if (!proceed) return;

            // Hide the badge immediately so it doesn't flash if relaunch is slow
            _portableBadge.Visibility = Visibility.Collapsed;

            App.InstallAndRelaunch(_currentFile, wantDesktop);
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // A long operation (compress, OCR, ...) is writing into its session; closing now
            // would tear the document out from under it. Same toast as a blocked tab switch.
            if (_longOps.IsBusy) { ShowToast(Loc("Str_Tab_Busy")); e.Cancel = true; return; }
            // Recorded before the dirty-prompt loop below switches `_s` from tab to tab while
            // asking about each one, so the OpenTabs setting remembers the tab the user was
            // actually looking at when they chose to close, not whichever one was asked about last.
            var activeBeforeClose = _s;
            // Text still being typed on the shown tab counts as an unsaved change. (Only the text
            // box is committed: a full DeactivateSession would cancel the shown tab's renders,
            // leaving it half drawn if the user then cancels the close.)
            try { CommitActiveTextBox(); } catch { }
            // Ask about every unsaved tab: the one being looked at first, then left to right.
            var dirty = TabCloseOrder.DirtyFirstActive(_tabs.Items, _s, t => t.IsDirty);
            foreach (var s in dirty)
            {
                if (_tabs.IndexOf(s) < 0 || !s.IsDirty) continue;
                SwitchTo(s);
                // SwitchTo swallows failures and can refuse; never prompt about (or save) a tab
                // that is not the one shown - Save acts on `_s`.
                if (!ReferenceEquals(_s, s)) { e.Cancel = true; return; }
                var res = ScalpelDialog.Show(this,
                    string.Format(Loc("Str_Tab_SavePrompt"), s.DisplayName),
                    Loc("Str_Dlg_AppTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (res == MessageBoxResult.Cancel || res == MessageBoxResult.None) { e.Cancel = true; return; }
                if (res == MessageBoxResult.Yes)
                {
                    if (!ReferenceEquals(_s, s)) SwitchTo(s);   // Save acts on the shown tab
                    if (!ReferenceEquals(_s, s)) { e.Cancel = true; return; }
                    SaveInPlace();
                    if (s.IsDirty) { e.Cancel = true; return; }   // save failed or Save As cancelled
                }
            }
            SaveWindowSettings(activeBeforeClose);
            base.OnClosing(e);
        }

    }
}
