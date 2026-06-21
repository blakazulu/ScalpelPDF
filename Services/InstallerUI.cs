using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scalpel.Services
{
    /// <summary>
    /// Branded, self-contained install/uninstall dialogs. Fixed dark+amber palette
    /// (no theme-dictionary dependency — these run before the main window). Custom
    /// borderless chrome with a draggable title bar.
    /// </summary>
    internal static class InstallerUI
    {
        // ── Brand palette ──────────────────────────────────────────────────
        private static SolidColorBrush B(byte r, byte g, byte b) =>
            new(Color.FromRgb(r, g, b));
        private static readonly Brush Canvas      = B(0x0A, 0x0B, 0x0E);
        private static readonly Brush Panel       = B(0x14, 0x16, 0x1A);
        private static readonly Brush Accent      = B(0xF2, 0xA9, 0x3B);
        private static readonly Brush AccentHover = B(0xF6, 0xC1, 0x70);
        private static readonly Brush TextPrimary = B(0xE7, 0xE9, 0xEE);
        private static readonly Brush TextDim     = B(0x7C, 0x81, 0x8C);
        private static readonly Brush Danger      = B(0xEF, 0x44, 0x44);
        private static readonly Brush DangerHover = B(0xF8, 0x71, 0x71);
        private static readonly FontFamily Geist  =
            new(new Uri("pack://application:,,,/"), "./Resources/Fonts/#Geist");

        // ── Public dialogs ─────────────────────────────────────────────────

        public static (bool proceed, bool wantDesktop) ShowInstallConfirm(bool alreadyInstalled)
        {
            bool proceed = false;
            bool desktop = true;

            var (win, content) = MakeWindow();

            content.Children.Add(Heading());
            content.Children.Add(Sub($"Version {VersionString()}"));
            content.Children.Add(Body(alreadyInstalled
                ? "Update Scalpel on this computer. Your settings are kept."
                : "Install Scalpel on this computer. Adds a Start-Menu entry and a PDF file association — no admin needed."));

            var desktopChk = new CheckBox
            {
                IsChecked = true,
                Margin    = new Thickness(0, 6, 0, 24),
                Foreground = TextPrimary,
                Content   = new TextBlock { Text = "Create desktop shortcut", Foreground = TextPrimary, FontFamily = Geist },
            };
            content.Children.Add(desktopChk);

            var primary = PrimaryButton(alreadyInstalled ? "Update" : "Install");
            var ghost   = GhostButton("Not now");
            primary.Click += (_, _) => { proceed = true; desktop = desktopChk.IsChecked == true; win.Close(); };
            ghost.Click   += (_, _) => { proceed = false; win.Close(); };
            content.Children.Add(ButtonRow(ghost, primary));

            win.ShowDialog();
            return (proceed, desktop);
        }

        public static bool RunUninstallFlow(Action inProcessWipe, Func<string> writeDeferredScript, Action launchScript)
        {
            bool proceed = false;
            var (win, content) = MakeWindow();

            content.Children.Add(Heading("Uninstall Scalpel"));
            content.Children.Add(Body(
                "Remove Scalpel and ALL of its data from this PC — the app, your settings, " +
                "saved signatures, and logs. Nothing is left behind. This cannot be undone."));

            var remove = DangerButton("Remove");
            var cancel = GhostButton("Cancel");
            cancel.Click += (_, _) => { proceed = false; win.Close(); };
            remove.Click += (_, _) =>
            {
                proceed = true;
                // Swap to the progress state in-place.
                content.Children.Clear();
                content.Children.Add(Heading("Removing Scalpel…"));
                content.Children.Add(Body("Cleaning up files and registry entries."));
                win.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    try { inProcessWipe(); } catch { }
                    try { writeDeferredScript(); launchScript(); } catch { }
                    // Farewell, then auto-close.
                    content.Children.Clear();
                    content.Children.Add(Heading("Done"));
                    content.Children.Add(Body("Thanks for using Scalpel."));
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (_, _) => { t.Stop(); win.Close(); };
                    t.Start();
                }));
            };
            content.Children.Add(ButtonRow(cancel, remove));

            win.ShowDialog();
            return proceed;
        }

        // ── Chrome + element factories ─────────────────────────────────────

        private static (Window win, StackPanel content) MakeWindow()
        {
            var win = new Window
            {
                Title                 = "Scalpel",
                Width                 = 420,
                SizeToContent         = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode            = ResizeMode.NoResize,
                WindowStyle           = WindowStyle.None,
                Background            = Canvas,
                AllowsTransparency    = false,
            };

            var root = new DockPanel();

            var titleBar = new DockPanel { Background = Panel, Height = 36 };
            DockPanel.SetDock(titleBar, Dock.Top);
            titleBar.MouseLeftButtonDown += (_, e) =>
            { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            var close = new Button
            {
                Content = "", // Tabler "x"
                FontFamily = new FontFamily(new Uri("pack://application:,,,/"), "./Resources/Fonts/#tabler-icons"),
                FontSize = 14, Width = 46, Foreground = TextDim,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Arrow,
            };
            close.Click += (_, _) => win.Close();
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(new TextBlock
            {
                Text = "Scalpel", Foreground = TextDim, FontFamily = Geist, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
            });
            root.Children.Add(titleBar);

            var content = new StackPanel { Margin = new Thickness(36, 26, 36, 30) };
            root.Children.Add(content);

            win.Content = root;
            return (win, content);
        }

        private static TextBlock Heading(string text = "Scalpel") => new()
        {
            Text = text, FontFamily = Geist, FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = Accent, Margin = new Thickness(0, 0, 0, 4),
        };
        private static TextBlock Sub(string text) => new()
        {
            Text = text, FontFamily = Geist, FontSize = 12, Foreground = TextDim,
            Margin = new Thickness(0, 0, 0, 16),
        };
        private static TextBlock Body(string text) => new()
        {
            Text = text, FontFamily = Geist, FontSize = 13, Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
        };

        private static StackPanel ButtonRow(params UIElement[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in buttons) row.Children.Add(b);
            return row;
        }

        private static Button PrimaryButton(string text) =>
            StyledButton(text, Accent, AccentHover, B(0x0A, 0x0A, 0x0A), width: 120, semibold: true);
        private static Button DangerButton(string text) =>
            StyledButton(text, Danger, DangerHover, Brushes.White, width: 120, semibold: true);
        private static Button GhostButton(string text) =>
            StyledButton(text, B(0x23, 0x27, 0x2F), B(0x2A, 0x2E, 0x36), TextPrimary, width: 96, semibold: false);

        private static Button StyledButton(string text, Brush normal, Brush hover, Brush fg, double width, bool semibold)
        {
            var template = new ControlTemplate(typeof(Button));
            var border   = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
            });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(0, 8, 0, 8));
            border.AppendChild(cp);
            template.VisualTree = border;

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, normal));
            style.Setters.Add(new Setter(Button.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trig.Setters.Add(new Setter(Button.BackgroundProperty, hover));
            style.Triggers.Add(trig);

            return new Button
            {
                Content = text, Width = width, Margin = new Thickness(8, 0, 0, 0),
                FontFamily = Geist, FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
                Style = style,
            };
        }

        private static string VersionString() =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
    }
}
