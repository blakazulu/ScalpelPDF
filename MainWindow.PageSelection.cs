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
        // Page selection handler
        // ============================================================

        // Lazy accessor — resolves PageList's internal ScrollViewer on first use.
        private ScrollViewer? _sidebarSv;
        private ScrollViewer? SidebarScrollViewer
            => _sidebarSv ??= FindDescendant<ScrollViewer>(PageList);

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T hit) return hit;
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void PageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SidebarScrollViewer?.ScrollToVerticalOffset(
                SidebarScrollViewer.VerticalOffset - e.Delta / 120.0 * WheelScrollAmount());
            e.Handled = true;
        }

        private void PageJumpBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _doc is null) return;
            e.Handled = true;
            if (int.TryParse(_pageJumpBox.Text, out int pg))
            {
                int idx = Math.Max(0, Math.Min(_doc.PageCount - 1, pg - 1));
                PageList.SelectedIndex = idx;
            }
            else
            {
                // Restore current page number if input was invalid
                _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
            }
            Keyboard.ClearFocus();
        }

        private void PageJumpBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _pageJumpBox.SelectAll();
        }

        private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageList.SelectedIndex >= 0)
            {
                CommitActiveTextBox();
                ClearSelection();
                ClearTextSelection();
                if (_viewMode == ViewMode.Continuous)
                {
                    _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                    if (!_suppressScrollToPage) ScrollContinuousToPage(PageList.SelectedIndex);
                    return;
                }
                if (_viewMode == ViewMode.Grid)
                {
                    // Grid is a stable overview: selecting a page highlights it but must NOT
                    // re-anchor the grid. It still needs an initial render (open / first display)
                    // when no tiles exist yet; later selections only update the highlight.
                    _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                    if (_pageContentPanel.Children.Count <= 1)
                    {
                        PagePreviewPanel.ScrollToTop();
                        PagePreviewPanel.ScrollToHorizontalOffset(0);
                        RenderPage(0);   // grid primary is always page 0
                        // Default the grid to a clean 3-columns-across fit. Deferred to Loaded so the
                        // viewport width is valid (it can still be 0 mid-open, which would fall back
                        // to a carried-over zoom and show a single large page).
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                            (Action)(() => SetZoom(GridZoomForN(Math.Min(_doc?.PageCount ?? 1, 3)))));
                    }
                    return;
                }
                PagePreviewPanel.ScrollToTop();
                PagePreviewPanel.ScrollToHorizontalOffset(0);
                RenderPage(PageList.SelectedIndex);
                ApplyZoom();
                // Update page jump box
                _pageJumpBox.Text = (PageList.SelectedIndex + 1).ToString();
                // Re-highlight search results on this page if a search is active
                if (_searchBar is not null && _searchBar.Visibility == Visibility.Visible
                    && _allSearchRects.Count > 0)
                    HighlightSearchResultsOnCurrentPage();
            }
        }

        private void ShortcutHelp_Click(object sender, RoutedEventArgs e)
        {
            SyncZoomShortcutLabel();   // same as F1 / Ctrl+?: print the zoom keys this layout types
            ShortcutOverlay.Visibility = ShortcutOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShortcutOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Click on the dim backdrop closes the overlay.
            ShortcutOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShortcutOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Stop the click from bubbling up to the backdrop handler.
            e.Handled = true;
        }

        private void ShortcutOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            ShortcutOverlay.Visibility = Visibility.Collapsed;
        }

        // ── About overlay ───────────────────────────────────────────────

        private void AboutTab_Click(object sender, RoutedEventArgs e) => ShowAboutOverlay();

        /// <summary>
        /// The build's release date for the About box. A real release has it stamped into
        /// BuildInfo by release.ps1; a dev build falls back to the executable's own timestamp so
        /// the field is still meaningful when testing.
        /// </summary>
        private static string ReleaseDateText()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(BuildInfo.ReleaseDate))
                    return BuildInfo.ReleaseDate;

                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe))
                    return System.IO.File.GetLastWriteTimeUtc(exe).ToString("yyyy-MM-dd");
            }
            catch { }
            return "";
        }

        private void ShowAboutOverlay()
        {
            // Populate dynamic values (SHA256 is slow; run on background thread)
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                               .GetName().Version?.ToString(3) ?? "?";
            var (sigValid, sigSubject, sigThumbprint) = App.GetExeSignerInfo();

            AboutPublisherBlock.Text   = sigValid ? sigSubject : "(not signed or chain failed)";
            AboutThumbprintBlock.Text  = string.IsNullOrEmpty(sigThumbprint) ? "(none)" : sigThumbprint;
            AboutSha256Block.Text      = Loc("Str_About_Computing");

            // Logo block
            AboutLogoBlock.Inlines.Clear();
            var logoHl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Scalpel"))
            {
                Foreground      = (System.Windows.Media.Brush)FindResource("Accent"),
                TextDecorations = null
            };
            logoHl.Click += (_, _) =>
                Process.Start(new ProcessStartInfo("https://github.com/blakazulu/ScalpelPDF") { UseShellExecute = true });
            AboutLogoBlock.Inlines.Add(logoHl);

            // Tagline block
            AboutTaglineBlock.Inlines.Clear();
            AboutTaglineBlock.Inlines.Add(new System.Windows.Documents.Run("A fast, free PDF toolkit for Windows.")
            {
                Foreground = (System.Windows.Media.Brush)FindResource("TextDim")
            });

            // Version block (clickable - opens GitHub release)
            AboutVersionBlock.Inlines.Clear();
            var verHl = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run($"v{version}"))
            {
                Foreground      = (System.Windows.Media.Brush)FindResource("Accent"),
                TextDecorations = null
            };
            verHl.Click += (_, _) =>
                Process.Start(new ProcessStartInfo(
                    $"https://github.com/blakazulu/ScalpelPDF/releases/tag/v{version}")
                { UseShellExecute = true });
            AboutVersionBlock.Inlines.Add(verHl);

            // Say WHEN this build was released. "Am I on an old copy?" is the question the About
            // box is usually opened to answer, and a version number alone does not answer it.
            string released = ReleaseDateText();
            if (released.Length > 0)
            {
                AboutVersionBlock.Inlines.Add(new System.Windows.Documents.Run($"  ({released})")
                {
                    Foreground = (System.Windows.Media.Brush)FindResource("TextDim")
                });
            }

            AboutOverlay.Visibility = Visibility.Visible;

            // Compute SHA256 off the UI thread
            System.Threading.Tasks.Task.Run(() =>
            {
                var sha256 = App.GetExeSha256();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    (Action)(() => AboutSha256Block.Text = sha256));
            });
        }

        private void AboutOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        private void AboutOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void AboutOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Collapsed;
        }

        // ── What's New (changelog) ─────────────────────────────────────────
        private void WhatsNewTab_Click(object sender, RoutedEventArgs e) => ShowWhatsNewOverlay();

        private void ShowWhatsNewOverlay()
        {
            WhatsNewList.Children.Clear();
            var accent = (System.Windows.Media.Brush)FindResource("Accent");
            var dim    = (System.Windows.Media.Brush)FindResource("TextDim");
            var body   = (System.Windows.Media.Brush)FindResource("TextPrimary");
            var uiFont = (FontFamily)FindResource("FontUI");

            bool first = true;
            foreach (var rel in Scalpel.Services.Changelog.Releases)
            {
                // Version + date header
                var header = new TextBlock
                {
                    FontFamily = uiFont, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = accent,
                    Margin = new Thickness(0, first ? 0 : 18, 0, 2),
                };
                header.Inlines.Add(new System.Windows.Documents.Run($"Version {rel.Version}"));
                header.Inlines.Add(new System.Windows.Documents.Run($"   ·   {rel.Date}")
                {
                    Foreground = dim,
                    FontWeight = FontWeights.Normal,
                });
                WhatsNewList.Children.Add(header);
                first = false;

                // Bulleted changes
                foreach (var change in rel.Changes)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                    var bullet = new TextBlock
                    {
                        Text = "•", FontFamily = uiFont, FontSize = 12, Foreground = accent,
                        Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                    };
                    DockPanel.SetDock(bullet, Dock.Left);
                    var text = new TextBlock
                    {
                        Text = change, FontFamily = uiFont, FontSize = 12, Foreground = body,
                        TextWrapping = TextWrapping.Wrap,
                    };
                    row.Children.Add(bullet);
                    row.Children.Add(text);
                    WhatsNewList.Children.Add(row);
                }
            }

            WhatsNewOverlay.Visibility = Visibility.Visible;
        }

        private void WhatsNewOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            WhatsNewOverlay.Visibility = Visibility.Collapsed;
        }

        private void WhatsNewOverlayCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void WhatsNewOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            WhatsNewOverlay.Visibility = Visibility.Collapsed;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

    }
}
