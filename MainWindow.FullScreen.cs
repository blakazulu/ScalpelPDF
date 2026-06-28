using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Scalpel
{
    public partial class MainWindow
    {
        private bool _fullScreen;
        private GridLength _fsRow0, _fsRow1, _fsRow2, _fsRow4, _fsSidebarW, _fsSplitterW;
        private double _fsSidebarMin;
        private WindowState _fsPrevState;
        private bool _fsPrevTopmost;
        private ResizeMode _fsPrevResize;
        private double _fsPrevLeft, _fsPrevTop, _fsPrevW, _fsPrevH;

        private void ToggleFullScreen() => ApplyFullScreen(!_fullScreen);

        private void ApplyFullScreen(bool entering)
        {
            _fullScreen = entering;
            var v = entering ? Visibility.Collapsed : Visibility.Visible;

            TitleBarBorder.Visibility   = v;
            RibbonTabBorder.Visibility  = v;
            RibbonBandBorder.Visibility = v;
            StatusBarBorder.Visibility  = v;
            _sidebarBorder.Visibility   = v;
            SidebarSplitter.Visibility  = v;

            if (entering)
            {
                _fsRow0 = RootGrid.RowDefinitions[0].Height;
                _fsRow1 = RootGrid.RowDefinitions[1].Height;
                _fsRow2 = RootGrid.RowDefinitions[2].Height;
                _fsRow4 = RootGrid.RowDefinitions[4].Height;
                RootGrid.RowDefinitions[0].Height = new GridLength(0);
                RootGrid.RowDefinitions[1].Height = new GridLength(0);
                RootGrid.RowDefinitions[2].Height = new GridLength(0);
                RootGrid.RowDefinitions[4].Height = new GridLength(0);

                _fsSidebarW   = _sidebarCol.Width;
                _fsSidebarMin = _sidebarCol.MinWidth;
                _fsSplitterW  = MainContentGrid.ColumnDefinitions[1].Width;
                _sidebarCol.MinWidth = 0;
                _sidebarCol.Width = new GridLength(0);
                MainContentGrid.ColumnDefinitions[1].Width = new GridLength(0);

                PagePreviewPanel.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));

                _fsPrevState = WindowState; _fsPrevTopmost = Topmost; _fsPrevResize = ResizeMode;
                _fsPrevLeft = Left; _fsPrevTop = Top; _fsPrevW = Width; _fsPrevH = Height;

                var b = CurrentMonitorBoundsDip();
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;

                ShowFullScreenHint();
            }
            else
            {
                RootGrid.RowDefinitions[0].Height = _fsRow0;
                RootGrid.RowDefinitions[1].Height = _fsRow1;
                RootGrid.RowDefinitions[2].Height = _fsRow2;
                RootGrid.RowDefinitions[4].Height = _fsRow4;
                _sidebarCol.MinWidth = _fsSidebarMin;
                _sidebarCol.Width = _fsSidebarW;
                MainContentGrid.ColumnDefinitions[1].Width = _fsSplitterW;
                PagePreviewPanel.Background = Brushes.Transparent;

                Topmost = _fsPrevTopmost;
                ResizeMode = _fsPrevResize;
                WindowState = WindowState.Normal;
                Left = _fsPrevLeft; Top = _fsPrevTop; Width = _fsPrevW; Height = _fsPrevH;
                if (_fsPrevState == WindowState.Maximized) WindowState = WindowState.Maximized;
            }
        }

        // Full monitor bounds (taskbar incl.) of the current monitor, in DIPs. Reuses the P/Invoke
        // (MonitorFromWindow/GetMonitorInfo/MONITORINFO/MONITOR_DEFAULTTONEAREST) declared in MainWindow.Settings.cs.
        private Rect CurrentMonitorBoundsDip()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(mon, ref info);
            var r = info.rcMonitor;
            var dpi = VisualTreeHelper.GetDpi(this);
            return new Rect(r.left / dpi.DpiScaleX, r.top / dpi.DpiScaleY,
                            (r.right - r.left) / dpi.DpiScaleX, (r.bottom - r.top) / dpi.DpiScaleY);
        }

        private void ShowFullScreenHint()
        {
            var toast = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x1c, 0x1c, 0x1c)),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(18, 9, 18, 9),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 44, 0, 0), Opacity = 0, IsHitTestVisible = false,
                Child = new TextBlock { Text = Loc("Str_FullScreen_Hint"), Foreground = Brushes.White, FontSize = 13 }
            };
            Grid.SetRow(toast, 0);
            Grid.SetRowSpan(toast, RootGrid.RowDefinitions.Count);
            Panel.SetZIndex(toast, 99999);
            RootGrid.Children.Add(toast);
            toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            t.Tick += (_, __) =>
            {
                t.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
                fade.Completed += (_, ___) => RootGrid.Children.Remove(toast);
                toast.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            t.Start();
        }
    }
}