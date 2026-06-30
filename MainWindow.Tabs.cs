using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scalpel
{
    public partial class MainWindow
    {
        // ============================================================
        // Document tabs (file-switcher strip)
        //
        // SAFE BY DESIGN: this tracks the SET of open file PATHS only — it does
        // NOT add a second document session. Switching tabs reuses the existing
        // open/close/dirty machinery (OpenFile reloads the target; SwitchToTab
        // runs the existing save-or-discard prompt first). When 0 or 1 file is
        // open the strip is Collapsed and no new logic engages, so single-document
        // behavior is byte-identical to before.
        // ============================================================

        // Open file paths in tab order. The ACTIVE tab is derived from _originalFile
        // (the user's real path for the loaded doc) — we never duplicate that state here.
        private readonly List<string> _openTabs = [];

        private static bool PathEq(string? a, string? b) =>
            string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        // Adds a freshly-opened file to the tab set. Called from FinishOpenFile.
        // Only real, on-disk paths get a tab (skips "Untitled.pdf" / temp new docs),
        // so a brand-new blank document never spawns an un-reopenable tab.
        private void AddTab(string? path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!File.Exists(path)) return;
                if (!_openTabs.Any(p => PathEq(p, path)))
                    _openTabs.Add(path!);
                RefreshTabStrip();
            }
            catch { /* tabs are a convenience layer — never break an open */ }
        }

        // Switches the active document to an already-open tab. Reuses the existing
        // dirty prompt (Str_Dlg_UnsavedClose) so unsaved edits are never silently lost.
        private void SwitchToTab(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (PathEq(path, _originalFile)) return; // already active — no-op

                if (!File.Exists(path))
                {
                    // Vanished from disk — drop it like OpenRecent does.
                    ShowToast(Loc("Str_Recent_NotFound"));
                    _openTabs.RemoveAll(p => PathEq(p, path));
                    RefreshTabStrip();
                    return;
                }

                if (_isDirty)
                {
                    var res = ScalpelDialog.Show(this,
                        Loc("Str_Dlg_UnsavedClose"),
                        "Scalpel", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (res != MessageBoxResult.Yes) return; // user cancelled the switch
                }

                OpenFile(path); // reuses the full open pipeline; FinishOpenFile re-marks active
            }
            catch { /* never crash on a tab switch */ }
        }

        // Closes a single tab. Background (non-active) tabs hold no unsaved state, so they
        // just drop out. Closing the ACTIVE tab moves to an adjacent tab (reusing SwitchToTab's
        // dirty prompt) or, if it was the last one, falls back to the existing CloseFile().
        private void CloseTab(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                int idx = _openTabs.FindIndex(p => PathEq(p, path));
                if (idx < 0) return;

                if (!PathEq(path, _originalFile))
                {
                    // Not the active document — nothing to save, just remove it.
                    _openTabs.RemoveAt(idx);
                    RefreshTabStrip();
                    return;
                }

                // Closing the active tab.
                if (_openTabs.Count > 1)
                {
                    string target = idx > 0 ? _openTabs[idx - 1] : _openTabs[idx + 1];
                    SwitchToTab(target); // handles the dirty prompt; may abort
                    if (!PathEq(_originalFile, target)) return; // switch was cancelled — keep the tab
                    _openTabs.RemoveAll(p => PathEq(p, path));
                    RefreshTabStrip();
                }
                else
                {
                    // Last tab — remove first so CloseFile() won't re-route back into CloseTab.
                    _openTabs.RemoveAt(idx);
                    CloseFile(); // existing full close (runs its own dirty prompt + empty state)
                }
            }
            catch { /* never crash on a tab close */ }
        }

        // Ctrl+Tab / Ctrl+Shift+Tab cycling. No-op unless 2+ tabs are open.
        private void CycleTab(bool forward)
        {
            try
            {
                if (_openTabs.Count <= 1) return;
                int idx = _openTabs.FindIndex(p => PathEq(p, _originalFile));
                if (idx < 0) idx = 0;
                int next = forward
                    ? (idx + 1) % _openTabs.Count
                    : (idx - 1 + _openTabs.Count) % _openTabs.Count;
                SwitchToTab(_openTabs[next]);
            }
            catch { }
        }

        // Rebuilds the chip row. Hidden unless 2+ files are open (single-doc no-op property).
        private void RefreshTabStrip()
        {
            try
            {
                if (TabStrip is null || TabStripHost is null) return;

                if (_openTabs.Count <= 1)
                {
                    TabStrip.Children.Clear();
                    TabStripHost.Visibility = Visibility.Collapsed;
                    return;
                }

                TabStripHost.Visibility = Visibility.Visible;
                TabStrip.Children.Clear();

                var fontUI = (FontFamily)Application.Current.FindResource("FontUI");
                var fontIcon = (FontFamily)Application.Current.FindResource("FontIcon");

                foreach (var path in _openTabs)
                {
                    bool active = PathEq(path, _originalFile);
                    TabStrip.Children.Add(BuildTabChip(path, active, fontUI, fontIcon));
                }
            }
            catch { /* a broken strip must never block editing */ }
        }

        private Border BuildTabChip(string path, bool active, FontFamily fontUI, FontFamily fontIcon)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(10, 3, 4, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = path
            };
            if (active)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "AccentDim");
                chip.SetResourceReference(Border.BorderBrushProperty, "Accent");
                chip.BorderThickness = new Thickness(1);
            }
            else
            {
                chip.SetResourceReference(Border.BackgroundProperty, "BgControl");
                chip.SetResourceReference(Border.BorderBrushProperty, "BorderDim");
                chip.BorderThickness = new Thickness(1);
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal };

            var label = new TextBlock
            {
                Text = System.IO.Path.GetFileName(path),
                FontFamily = fontUI,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 6, 0)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, active ? "Accent" : "TextPrimary");
            row.Children.Add(label);

            var closeBtn = new Button
            {
                Style = (Style)Application.Current.FindResource("StudioIconButton"),
                Content = Application.Current.FindResource("Ico_WinClose"),
                FontFamily = fontIcon,
                FontSize = 11,
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc("Str_TT_CloseTab"),
                Tag = path
            };
            closeBtn.Click += (s, e) =>
            {
                e.Handled = true;
                if (s is FrameworkElement fe && fe.Tag is string p) CloseTab(p);
            };
            row.Children.Add(closeBtn);

            chip.Child = row;
            string captured = path;
            chip.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                SwitchToTab(captured);
            };
            return chip;
        }
    }
}
