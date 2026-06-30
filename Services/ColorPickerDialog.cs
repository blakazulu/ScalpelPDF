using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Scalpel.Services
{
    /// <summary>
    /// Scalpel-styled modal RGB color picker: an HSV saturation/value square + vertical hue strip,
    /// live R/G/B and #hex inputs, a color preview, and a desktop-wide eyedropper. Opaque RGB only —
    /// opacity stays with the toolbar's own slider. Built in code to match <c>ScalpelDialog</c>'s
    /// look (dark card, AccentBorder, Geist font, rounded corners).
    /// </summary>
    internal sealed class ColorPickerDialog : Window
    {
        public Color SelectedColor { get; private set; }

        private double _h, _s = 1, _v = 1;   // HSV state (h 0..360, s/v 0..1)
        private bool _updating;              // guards the field<->thumb<->preview sync from feedback loops

        private Rectangle _svHue = null!;
        private Canvas _svThumb = null!;
        private Border _hueThumb = null!;
        private Border _preview = null!;
        private TextBox _rBox = null!, _gBox = null!, _bBox = null!, _hexBox = null!;

        private const int SvW = 220, SvH = 170, HueW = 18;

        private static SolidColorBrush R(string key) => (SolidColorBrush)Application.Current.Resources[key];
        private static string L(string key) => Application.Current.TryFindResource(key) as string ?? key;

        public ColorPickerDialog(Window? owner, Color initial)
        {
            Title = L("Str_ColorPicker_Title");
            Width = 300;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;
            Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            FontFamily = (FontFamily)Application.Current.FindResource("FontUI");

            SelectedColor = Color.FromRgb(initial.R, initial.G, initial.B);
            (_h, _s, _v) = ColorConvert.RgbToHsv(SelectedColor);

            BuildUi();
            SyncFromHsv();

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; Close(); }
                else if (e.Key == Key.Enter) Accept();
            };
        }

        private void BuildUi()
        {
            var card = new Border
            {
                Background = R("BgModal"),
                BorderBrush = R("AccentBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4, Direction = 270 }
            };

            var panel = new StackPanel();

            // Title bar (draggable)
            var titleBar = new Border
            {
                Background = R("BgPanel"),
                Padding = new Thickness(16, 10, 16, 10),
                CornerRadius = new CornerRadius(11, 11, 0, 0),
                Child = new TextBlock
                {
                    Text = L("Str_ColorPicker_Title"),
                    Foreground = R("Accent"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14
                }
            };
            titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            panel.Children.Add(titleBar);

            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
            panel.Children.Add(body);

            // ── SV square + hue strip ───────────────────────────────────────────
            var pickRow = new StackPanel { Orientation = Orientation.Horizontal };

            _svHue = new Rectangle { Width = SvW, Height = SvH };
            var svWhite = new Rectangle
            {
                Width = SvW, Height = SvH, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0)
            };
            var svBlack = new Rectangle
            {
                Width = SvW, Height = SvH, IsHitTestVisible = false,
                Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), 90)
            };
            _svThumb = new Canvas { Width = SvW, Height = SvH, IsHitTestVisible = false };
            var svDot = new Ellipse
            {
                Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.8 }
            };
            _svThumb.Children.Add(svDot);

            var svGrid = new Grid { Width = SvW, Height = SvH };
            svGrid.Children.Add(_svHue);
            svGrid.Children.Add(svWhite);
            svGrid.Children.Add(svBlack);
            svGrid.Children.Add(_svThumb);

            var svArea = new Border
            {
                Width = SvW, Height = SvH, CornerRadius = new CornerRadius(3), ClipToBounds = false,
                BorderBrush = R("BorderDim"), BorderThickness = new Thickness(1), Child = svGrid, Cursor = Cursors.Cross
            };
            svArea.MouseLeftButtonDown += (_, e) => { svArea.CaptureMouse(); SvPick(e.GetPosition(svGrid)); };
            svArea.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) SvPick(e.GetPosition(svGrid)); };
            svArea.MouseLeftButtonUp += (_, _) => svArea.ReleaseMouseCapture();
            pickRow.Children.Add(svArea);

            var hueRect = new Rectangle { Width = HueW, Height = SvH, Fill = HueStripBrush() };
            _hueThumb = new Border
            {
                Width = HueW + 6, Height = 6, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                Background = R("Accent"), CornerRadius = new CornerRadius(2), IsHitTestVisible = false
            };
            var hueCanvas = new Canvas { Width = HueW + 6, Height = SvH };
            Canvas.SetLeft(_hueThumb, -3);
            hueCanvas.Children.Add(_hueThumb);
            var hueGrid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            hueGrid.Children.Add(hueRect);
            hueGrid.Children.Add(hueCanvas);
            var hueArea = new Border
            {
                Child = hueGrid, Cursor = Cursors.SizeNS, CornerRadius = new CornerRadius(3),
                BorderBrush = R("BorderDim"), BorderThickness = new Thickness(1)
            };
            hueArea.MouseLeftButtonDown += (_, e) => { hueArea.CaptureMouse(); HuePick(e.GetPosition(hueRect)); };
            hueArea.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) HuePick(e.GetPosition(hueRect)); };
            hueArea.MouseLeftButtonUp += (_, _) => hueArea.ReleaseMouseCapture();
            pickRow.Children.Add(hueArea);

            body.Children.Add(pickRow);

            // ── Preview + RGB + eyedropper ──────────────────────────────────────
            var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _preview = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(3),
                BorderBrush = R("BorderDim"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0)
            };
            inputRow.Children.Add(_preview);

            _rBox = NumBox(); _gBox = NumBox(); _bBox = NumBox();
            inputRow.Children.Add(FieldGroup("R", _rBox));
            inputRow.Children.Add(FieldGroup("G", _gBox));
            inputRow.Children.Add(FieldGroup("B", _bBox));

            var eyedrop = new Button
            {
                Width = 28, Height = 22, Margin = new Thickness(8, 14, 0, 0),
                Background = R("BgHover"), BorderBrush = R("BorderDim"), BorderThickness = new Thickness(1),
                Content = CrosshairIcon(), ToolTip = L("Str_ColorPicker_Eyedropper"), Cursor = Cursors.Cross,
                Template = MakeBtnTemplate()
            };
            eyedrop.Click += (_, _) => RunEyedropper();
            inputRow.Children.Add(eyedrop);
            body.Children.Add(inputRow);

            // ── Hex ─────────────────────────────────────────────────────────────
            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            hexRow.Children.Add(new TextBlock
            {
                Text = "Hex", Foreground = R("TextSecondary"), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            _hexBox = MakeTextBox(96);
            _hexBox.MaxLength = 7;
            _hexBox.LostFocus += (_, _) => CommitHex();
            _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
            hexRow.Children.Add(_hexBox);
            body.Children.Add(hexRow);

            // ── OK / Cancel ─────────────────────────────────────────────────────
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button
            {
                Content = "Cancel", Style = (Style)Application.Current.FindResource("StudioToolButton"),
                Width = 80, Margin = new Thickness(8, 0, 0, 0)
            };
            cancel.Click += (_, _) => { DialogResult = false; Close(); };
            var ok = new Button
            {
                Content = "OK", Style = (Style)Application.Current.FindResource("StudioPrimaryButton"),
                Width = 80, Margin = new Thickness(8, 0, 0, 0)
            };
            ok.Click += (_, _) => Accept();
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            body.Children.Add(btnRow);

            card.Child = panel;
            Content = card;
        }

        private void Accept()
        {
            SelectedColor = ColorConvert.HsvToColor(_h, _s, _v);
            DialogResult = true;
            Close();
        }

        // ── Interaction ─────────────────────────────────────────────────────────
        private void SvPick(Point p) { _s = Clamp01(p.X / SvW); _v = Clamp01(1 - p.Y / SvH); SyncFromHsv(); }
        private void HuePick(Point p) { _h = Clamp01(p.Y / SvH) * 360; SyncFromHsv(); }
        private void CommitHex() { if (ColorConvert.TryParseHex(_hexBox.Text, out Color c)) SetFromColor(c); else SyncFromHsv(); }

        private void CommitRgb()
        {
            if (byte.TryParse(_rBox.Text, out byte r) && byte.TryParse(_gBox.Text, out byte g) && byte.TryParse(_bBox.Text, out byte b))
                SetFromColor(Color.FromRgb(r, g, b));
            else SyncFromHsv();
        }

        private void SetFromColor(Color c) { (_h, _s, _v) = ColorConvert.RgbToHsv(c); SyncFromHsv(); }

        // Push current HSV out to every control (hue background, thumbs, RGB, hex, preview).
        private void SyncFromHsv()
        {
            if (_updating) return;
            _updating = true;
            var c = ColorConvert.HsvToColor(_h, _s, _v);
            _svHue.Fill = new SolidColorBrush(ColorConvert.HsvToColor(_h, 1, 1));
            Canvas.SetLeft((UIElement)_svThumb.Children[0], _s * SvW - 6);
            Canvas.SetTop((UIElement)_svThumb.Children[0], (1 - _v) * SvH - 6);
            Canvas.SetTop(_hueThumb, Math.Max(0, Math.Min(SvH - 6, _h / 360.0 * SvH - 3)));
            _rBox.Text = c.R.ToString(); _gBox.Text = c.G.ToString(); _bBox.Text = c.B.ToString();
            _hexBox.Text = ColorConvert.ToHex(c);
            _preview.Background = new SolidColorBrush(c);
            _updating = false;
        }

        // ── Eyedropper (desktop-wide) ───────────────────────────────────────────
        private void RunEyedropper()
        {
            try
            {
                var capture = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Cursor = Cursors.Cross,
                    Left = SystemParameters.VirtualScreenLeft, Top = SystemParameters.VirtualScreenTop,
                    Width = SystemParameters.VirtualScreenWidth, Height = SystemParameters.VirtualScreenHeight, Owner = this
                };
                capture.MouseLeftButtonDown += (_, _) =>
                {
                    try
                    {
                        // GetCursorPos returns physical screen pixels; the desktop DC's GetPixel uses the
                        // same space, so this is correct regardless of per-monitor DPI scaling.
                        if (GetCursorPos(out POINT pt))
                        {
                            IntPtr dc = GetDC(IntPtr.Zero);
                            uint cref = GetPixel(dc, pt.X, pt.Y);
                            ReleaseDC(IntPtr.Zero, dc);
                            capture.DialogResult = true; capture.Close();
                            SetFromColor(Color.FromRgb((byte)(cref & 0xFF), (byte)((cref >> 8) & 0xFF), (byte)((cref >> 16) & 0xFF)));
                            return;
                        }
                    }
                    catch { }
                    capture.DialogResult = false; capture.Close();
                };
                capture.KeyDown += (_, e) => { if (e.Key == Key.Escape) { capture.DialogResult = false; capture.Close(); } };
                capture.ShowDialog();
            }
            catch { }
        }

        // ── Small themed control builders ───────────────────────────────────────
        private StackPanel FieldGroup(string label, TextBox box)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            sp.Children.Add(new TextBlock { Text = label, Foreground = R("TextSecondary"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(box);
            return sp;
        }

        private TextBox NumBox()
        {
            var b = MakeTextBox(34);
            b.MaxLength = 3;
            b.TextAlignment = TextAlignment.Center;
            b.LostFocus += (_, _) => CommitRgb();
            b.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitRgb(); };
            return b;
        }

        private TextBox MakeTextBox(double width) => new()
        {
            Width = width, Height = 22, VerticalContentAlignment = VerticalAlignment.Center,
            Background = R("BgHover"), Foreground = R("TextPrimary"),
            BorderBrush = R("BorderDim"), BorderThickness = new Thickness(1),
            CaretBrush = R("TextPrimary"), SelectionBrush = R("AccentDim"),
            Padding = new Thickness(4, 0, 4, 0), Template = MakeTextBoxTemplate()
        };

        private UIElement CrosshairIcon()
        {
            var g = new Grid { Width = 14, Height = 14 };
            var fg = R("TextPrimary");
            g.Children.Add(new Rectangle { Width = 1.4, Fill = fg, HorizontalAlignment = HorizontalAlignment.Center });
            g.Children.Add(new Rectangle { Height = 1.4, Fill = fg, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Ellipse
            {
                Width = 8, Height = 8, Stroke = fg, StrokeThickness = 1.4,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Fill = Brushes.Transparent
            });
            return g;
        }

        private static ControlTemplate MakeTextBoxTemplate()
        {
            var b = new FrameworkElementFactory(typeof(Border));
            foreach (var (dp, prop) in new[] { (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness") })
                b.SetBinding(dp, new Binding(prop) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var sv = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            sv.SetValue(ScrollViewer.VerticalAlignmentProperty, VerticalAlignment.Center);
            b.AppendChild(sv);
            return new ControlTemplate(typeof(TextBox)) { VisualTree = b };
        }

        private static ControlTemplate MakeBtnTemplate()
        {
            var bf = new FrameworkElementFactory(typeof(Border));
            foreach (var (dp, prop) in new[] { (Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush"), (Border.BorderThicknessProperty, "BorderThickness") })
                bf.SetBinding(dp, new Binding(prop) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            bf.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bf.AppendChild(cp);
            return new ControlTemplate(typeof(Button)) { VisualTree = bf };
        }

        private static LinearGradientBrush HueStripBrush()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(ColorConvert.HsvToColor(i * 60, 1, 1), i / 6.0));
            return g;
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);
    }
}
