using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace KillerPDF.Services
{
    internal enum Theme { Dark, Light, HighContrast, Blood, Greed, Cyanotic }

    internal static class ThemeManager
    {
        // ── P/Invoke ──────────────────────────────────────────────────────

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        // ── State ─────────────────────────────────────────────────────────

        private static Theme _current = Theme.Dark;

        public static Theme Current => _current;

        /// <summary>Fired after the theme dictionary has been updated.</summary>
        public static event Action? ThemeChanged;

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Call once at startup (before MainWindow is created) to restore the saved theme.
        /// DWM title bar is applied later via ApplyDwm(hwnd) from SourceInitialized.
        /// </summary>
        public static void Initialize()
        {
            var saved = App.GetSetting("Theme");
            _current = Enum.TryParse<Theme>(saved, out var t) ? t : Theme.Dark;
            ApplyInternal(_current, applyDwm: false);
        }

        /// <summary>
        /// Change to a new theme, persist the choice, and update DWM immediately.
        /// </summary>
        public static void Apply(Theme theme)
        {
            _current = theme;
            App.SetSetting("Theme", theme.ToString());
            ApplyInternal(theme, applyDwm: true);
            ThemeChanged?.Invoke();
        }

        /// <summary>
        /// Called from Window.SourceInitialized to set the native title bar colour.
        /// </summary>
        public static void ApplyDwm(IntPtr hwnd)
        {
            SetDwm(hwnd, _current != Theme.Light);
        }

        // ── Internal ─────────────────────────────────────────────────────

        private static void ApplyInternal(Theme theme, bool applyDwm)
        {
            LoadDict(theme);

            if (applyDwm)
            {
                var win = Application.Current?.MainWindow;
                if (win != null)
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd != IntPtr.Zero)
                        SetDwm(hwnd, theme != Theme.Light);
                }
            }
        }

        private static void LoadDict(Theme theme)
        {
            var uri = theme switch
            {
                Theme.Light        => new Uri("pack://application:,,,/Themes/Light.xaml"),
                Theme.HighContrast => new Uri("pack://application:,,,/Themes/HighContrast.xaml"),
                Theme.Blood        => new Uri("pack://application:,,,/Themes/Blood.xaml"),
                Theme.Greed        => new Uri("pack://application:,,,/Themes/Greed.xaml"),
                Theme.Cyanotic     => new Uri("pack://application:,,,/Themes/Cyanotic.xaml"),
                _                  => new Uri("pack://application:,,,/Themes/Dark.xaml"),
            };

            var newDict = new ResourceDictionary { Source = uri };
            var merged  = Application.Current.Resources.MergedDictionaries;

            // In-place per-key update: fires a targeted notification for each changed key without
            // structurally modifying MergedDictionaries. Structural add/remove fires a synchronous
            // ResourcesChanged that can invoke FindResource() calls (e.g. in SwitchSidebarToPagesTab)
            // before the new dict is fully in place, causing ResourceReferenceKeyNotFoundException.
            if (merged.Count > 0)
            {
                var existing = merged[0];
                foreach (object key in newDict.Keys)
                    existing[key] = newDict[key];
            }
            else
            {
                merged.Add(newDict);
            }

            // One SystemIdle pass to nudge any elements whose effective value didn't auto-update
            // (e.g. ControlTemplate trigger bindings with TargetName that missed the per-key signal).
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, (Action)RefreshIcons);
        }

        /// <summary>
        /// Call from MainWindow.ContentRendered to fix icon colours on initial load
        /// when the theme was restored from settings (no switch event fires).
        /// </summary>
        public static void RefreshIcons()
        {
            if (Application.Current == null) return;
            foreach (Window w in Application.Current.Windows)
                ForceRender(w);
        }

        private static void ForceRender(DependencyObject node)
        {
            if (node is System.Windows.Controls.Primitives.ToggleButton tb)
            {
                // ClearValue + InvalidateProperty forces style-setter DynamicResources to
                // re-resolve from the updated dictionary without firing Checked/Unchecked
                // event handlers (which would re-trigger Apply and cause an infinite loop).
                tb.ClearValue(Control.ForegroundProperty);
                tb.InvalidateProperty(Control.ForegroundProperty);
            }
            if (node is Control ctrl)
            {
                ctrl.InvalidateProperty(Control.ForegroundProperty);
                ctrl.InvalidateProperty(Control.BackgroundProperty);
                ctrl.InvalidateProperty(Control.BorderBrushProperty);
            }
            if (node is UIElement el) el.InvalidateVisual();
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                ForceRender(VisualTreeHelper.GetChild(node, i));
        }

        private static void SetDwm(IntPtr hwnd, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch { /* DWMWA not supported on older Windows builds */ }
        }
    }
}
